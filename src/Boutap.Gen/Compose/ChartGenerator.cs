// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Boutap.Core.Common;
using Boutap.Core.Determinism;
using Boutap.Core.Pack;
using Boutap.Gen.Analysis;
using Boutap.Gen.Dsp;

namespace Boutap.Gen.Compose;

/// <summary>Ce qu'un niveau retient de la grille.</summary>
/// <param name="Level">Niveau vise.</param>
/// <param name="KeptSubdivisions">
/// Nombre de subdivisions du temps jouables, parmi les
/// <see cref="Quantizer.SubdivisionsPerBeat"/> du quantificateur. Le niveau joue
/// une subdivision sur <c>16 / KeptSubdivisions</c>.
/// </param>
/// <remarks>
/// Les trois niveaux ne sont pas « facile, moyen, difficile » mais « le meme
/// morceau, lu autrement » : berceau ne joue que le temps fort, ronde ajoute les
/// contre-temps, cascade accepte la syncope. Un niveau n'est donc jamais une
/// sous-partie de l'autre, et c'est ce que le joueur peut rejouer.
/// <para>
/// Le nombre de subdivisions, et non leur position, est le bouton de densite.
/// Filtrer sur la position rendait les niveaux inverses : le quantificateur
/// pose l'immense majorite des onsets sur la subdivision 0, et un niveau qui
/// l'exclutait se retrouvait avec moins de notes que le plus simple des trois.
/// </para>
/// </remarks>
public sealed record LevelProfile(ChartLevel Level, int KeptSubdivisions);

/// <summary>Tout ce que le generateur doit savoir sur un morceau.</summary>
/// <param name="SampleRate">Frequence d'echantillonnage de l'audio analyse.</param>
/// <param name="DurationSeconds">Duree de l'audio.</param>
/// <param name="Analysis">Resultat de l'analyse spectrale.</param>
/// <param name="OnsetBands">Flux d'onsets bande par bande.</param>
/// <param name="Beats">Battements de reference.</param>
/// <param name="Key">Tonalite la plus probable, ou <c>null</c> si rien ne se degage.</param>
public sealed record TrackProfile(
    double SampleRate,
    double DurationSeconds,
    SpectralAnalysis Analysis,
    OnsetBands OnsetBands,
    BeatTrack Beats,
    KeyCandidate? Key);

/// <summary>Reglages de generation.</summary>
/// <param name="Version">Version du generateur inscrite dans le pack.</param>
/// <param name="AudioSha256Hex">Empreinte de l'audio decode.</param>
/// <param name="Profiles">Les niveaux a generer.</param>
public sealed record ChartGeneratorOptions(
    string Version,
    string AudioSha256Hex,
    IReadOnlyList<LevelProfile> Profiles)
{
    /// <summary>Seuils de detection des pics.</summary>
    public PeakPickerOptions PeakPicker { get; init; } = PeakPickerOptions.Specification;

    /// <summary>Reglages du suivi de battement.</summary>
    public BeatTrackerOptions BeatTracker { get; init; } = BeatTrackerOptions.Specification;

    /// <summary>Temps de reaction et de transition du joueur simule.</summary>
    public PlayerSimulatorOptions Player { get; init; } = PlayerSimulatorOptions.Adult;

    /// <summary>Amplitude de l'humanisation.</summary>
    public HumanizerOptions Humanizer { get; init; } = HumanizerOptions.Specification;

    /// <summary>Les trois niveaux de la V1, du plus simple au plus dense.</summary>
    public static IReadOnlyList<LevelProfile> DefaultProfiles { get; } =
    [
        new LevelProfile(ChartLevel.Berceau, KeptSubdivisions: 1),
        new LevelProfile(ChartLevel.Ronde, KeptSubdivisions: 4),
        new LevelProfile(ChartLevel.Cascade, KeptSubdivisions: 8),
    ];
}

/// <summary>Le contenu d'un niveau, avant ecriture.</summary>
/// <param name="Level">Niveau vise.</param>
/// <param name="Notes">Notes retenues, triees.</param>
/// <param name="Removed">Notes retirees pour lisibilite, avec le motif.</param>
/// <param name="Unreachable">Notes retirees parce qu'aucun joueur moyen ne pourrait les frapper.</param>
/// <param name="Strain">Courbe d'effort.</param>
public sealed record ChartDraft(
    ChartLevel Level,
    IReadOnlyList<Note> Notes,
    IReadOnlyList<ReadabilityFinding> Removed,
    IReadOnlyList<UnreachableNote> Unreachable,
    StrainProfile Strain);

/// <summary>Fabrique un pack complet a partir d'un audio deja analyse.</summary>
/// <remarks>
/// L'ordre des etapes n'est pas un detail : lisibilite d'abord, puis
/// jouabilite, puis humanisation, puis nettoyage. Humaniser avant de filtrer
/// laisserait passer des rafales infranchissables que le filtre aurait du voir,
/// et nettoyer avant d'humaniser Casserait le determinisme.
/// </remarks>
public static class ChartGenerator
{
    /// <summary>Nom inscrit dans <c>generator.name</c>.</summary>
    public const string Name = "boutap-gen";

    /// <summary>Genere un niveau.</summary>
    /// <param name="track">Ce que l'analyse a etabli sur le morceau.</param>
    /// <param name="profile">Ce que ce niveau retient de la grille.</param>
    /// <param name="options">Reglages et empreinte de l'audio.</param>
    public static ChartDraft Generate(TrackProfile track, LevelProfile profile, ChartGeneratorOptions options)
    {
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(options);

        SpectralAnalysis analysis = track.Analysis;
        double hopSeconds = AnalysisSettings.HopLength / track.SampleRate;

        IReadOnlyList<Onset> onsets = PeakPicker.Pick(
            analysis.Onset, track.SampleRate, AnalysisSettings.HopLength, options.PeakPicker);
        IReadOnlyList<BeatPhase> phases = BeatTracker.MeasurePhases(
            analysis.Onset, track.Beats.BeatsSeconds, track.Beats.TempoBpm, hopSeconds);
        IReadOnlyList<GridTime> grid = Quantizer.Quantize(
            onsets, track.Beats.BeatsSeconds, track.Beats.TempoBpm, phases);

        var drafted = new List<Note>(grid.Count * 3);
        foreach (GridTime time in grid)
        {
            if (!IsSelected(time.Subdivision, profile))
            {
                continue;
            }

            ReadOnlySpan<double> chroma = ChromaAt(analysis, time.Seconds, hopSeconds);
            Chord chord = ChooseChord(chroma, track.Key);
            int force = ForceFrom(time.Strength);
            double window = WindowScaleFrom(ChordMapper.Score(chroma, chord));

            foreach (int gridIndex in chord.GridNotes)
            {
                drafted.Add(new Note
                {
                    Time = time.Seconds,
                    Key = KeyBinding.Grid(gridIndex),
                    Force = force,
                    WindowScale = window,
                });
            }
        }

        drafted.Sort(CompareNotes);

        ReadabilityReport smoothed = ReadabilityFilter.Smooth(drafted);
        SimulationReport played = PlayerSimulator.Simulate(smoothed.Notes, profile.Level, options.Player);
        IReadOnlyList<Note> humanized = Humanizer.Humanize(
            played.Reachable, options.AudioSha256Hex, options.Version, profile.Level, track.Beats.TempoBpm, options.Humanizer);
        IReadOnlyList<Note> cleaned = NoteCleaner.Clean(
            humanized, track.DurationSeconds, onsets.Count == 0 ? 0 : onsets[0].TimeSeconds);

        return new ChartDraft(
            profile.Level, cleaned, smoothed.Findings, played.Unreachable, StrainCurve.Compute(cleaned));
    }

    /// <summary>Transforme un projet de niveau en objet de chart, pret a etre ecrit.</summary>
    /// <param name="draft">Le contenu du niveau.</param>
    /// <param name="audioSha256Hex">Empreinte de l'audio decode.</param>
    /// <param name="version">Version du generateur.</param>
    /// <param name="offsetSeconds">Decalage entre l'audio et la premiere note.</param>
    public static Chart ToChart(ChartDraft draft, string audioSha256Hex, string version, double offsetSeconds = 0)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(audioSha256Hex);
        ArgumentNullException.ThrowIfNull(version);

        return new Chart
        {
            Schema = PackFormat.ChartSchema,
            Level = draft.Level,
            AudioSha256 = audioSha256Hex,
            OffsetSeconds = offsetSeconds,
            Generator = new GeneratorRef
            {
                Name = Name,
                Version = version,
                Seed = SeedDerivation.ChartSeedValue(audioSha256Hex, version, draft.Level.FileNameOf()),
            },
            Notes = draft.Notes,
        };
    }

    /// <summary>La fenetre de jugement multiplies une note de facteur <paramref name="scale"/>.</summary>
    public static double RescaleWindow(double baseSeconds, double scale) => baseSeconds * scale;

    private static int CompareNotes(Note left, Note right)
    {
        int byTime = left.Time.CompareTo(right.Time);
        return byTime != 0
            ? byTime
            : string.CompareOrdinal(left.Key.ToString(), right.Key.ToString());
    }

    private static bool IsSelected(int subdivision, LevelProfile profile)
    {
        if (subdivision < 0 || subdivision >= Quantizer.SubdivisionsPerBeat)
        {
            return false;
        }

        if (profile.KeptSubdivisions <= 0)
        {
            return false;
        }

        int step = Math.Max(1, Quantizer.SubdivisionsPerBeat / profile.KeptSubdivisions);
        return (subdivision % step) == 0;
    }

    /// <summary>Choisit l'accord d'un instant donne.</summary>
    /// <remarks>
    /// La tonalite du morceau n'est qu'un tie-breaker : elle departage les
    /// accords a energie egale, elle n'impose rien. Imposer la tonique ferait
    /// entendre un accord faux des qu'un modulation passe, et le joueur entend
    /// une modulation.
    /// </remarks>
    private static Chord ChooseChord(ReadOnlySpan<double> chroma, KeyCandidate? key)
    {
        if (key is null)
        {
            return ChordMapper.Best(chroma);
        }

        Chord best = ChordMapper.Best(chroma);
        int pitchClass = key.Value.Key.PitchClass;
        if (best.Root == pitchClass)
        {
            return best;
        }

        // A energie comparable, on privilegie l'accord de la tonalite annoncee.
        Chord inKey = ChordMapper.Of(pitchClass, best.Mode);
        return ChordMapper.Score(chroma, inKey) >= (ChordMapper.Score(chroma, best) * 0.9)
            ? inKey
            : best;
    }

    private static ReadOnlySpan<double> ChromaAt(SpectralAnalysis analysis, double seconds, double hopSeconds)
    {
        int frame = (int)Math.Round(seconds / hopSeconds, MidpointRounding.AwayFromZero);
        int frames = analysis.FrameCount;
        if (frame < 0)
        {
            frame = 0;
        }

        if (frame >= frames)
        {
            frame = frames - 1;
        }

        return analysis.Chroma.AsSpan(frame * SpectralAnalysis.ClassCount, SpectralAnalysis.ClassCount);
    }

    private static int ForceFrom(double strength)
    {
        double clamped = strength < 0 ? 0 : (strength > 1 ? 1 : strength);
        return (int)Math.Round(clamped * PackFormat.DefaultForce, MidpointRounding.AwayFromZero);
    }

    private static double WindowScaleFrom(double score)
    {
        if (!double.IsFinite(score) || score <= 0)
        {
            return PackFormat.DefaultWindowScale;
        }

        // Un accord net se joue avec une fenetre etroite, un accord flou avec une
        // fenetre large : la tolerance suit la confiance, pas l'inverse.
        double scaled = 1.0 + (Math.Min(1.0, 1.0 - score) * 0.5);
        return scaled < 0.5 ? 0.5 : Math.Min(3.0, scaled);
    }
}
