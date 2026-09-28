// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json.Nodes;
using Boutap.Core.Common;
using Boutap.Core.Determinism;
using Boutap.Core.Pack;
using Boutap.Gen.Analysis;
using Boutap.Gen.Dsp;

namespace Boutap.Gen.Compose;

/// <summary>Ce qu'il faut savoir pour nommer un pack.</summary>
/// <param name="Id">Identifiant du pack, en minuscules.</param>
/// <param name="Title">Titre affiche.</param>
/// <param name="Artist">Artiste, ou une chaine vide.</param>
/// <param name="ContentLicense">Expression SPDX de la licence du contenu.</param>
/// <param name="AudioPath">Entree de l'audio dans le pack.</param>
public sealed record PackIdentity(
    string Id,
    string Title,
    string Artist,
    string ContentLicense,
    string AudioPath);

/// <summary>Le resultat complet d'une generation.</summary>
/// <param name="Track">Ce que l'analyse a etabli.</param>
/// <param name="Drafts">Un projet par niveau.</param>
/// <param name="Options">Les reglages utilises, graine comprise.</param>
public sealed record GenerationResult(
    TrackProfile Track,
    IReadOnlyList<ChartDraft> Drafts,
    ChartGeneratorOptions Options);

/// <summary>Enchaine analyse et generation.</summary>
/// <remarks>
/// C'est ici que l'on s'engage : une generation doit etre rejouable a partir de
/// la seule graine inscrite dans le pack. Tout ce qui suit lit donc l'audio et la
/// graine, jamais l'heure du systeme ni le nom du fichier.
/// </remarks>
public static class Pipeline
{
    /// <summary>Analyse un audio puis genere les niveaux demandes.</summary>
    /// <param name="samples">Signal mono, dans la forme canonique.</param>
    /// <param name="sampleRate">Frequence d'echantillonnage.</param>
    /// <param name="durationSeconds">Duree reelle de l'audio.</param>
    /// <param name="audioSha256Hex">Empreinte de l'audio decode.</param>
    /// <param name="version">Version du generateur.</param>
    /// <param name="profiles">Niveaux a generer, ou <see langword="null"/> pour les trois.</param>
    /// <param name="seedHex">
    /// Materiau de graine impose, ou <see langword="null"/> pour deriver les
    /// graines de l'empreinte de l'audio. Il ne remplace pas l'empreinte :
    /// <c>audio.sha256</c> et <c>chart.audio_sha256</c> restent l'empreinte du
    /// contenu decode, quelle que soit cette valeur.
    /// </param>
    public static GenerationResult Run(
        double[] samples,
        int sampleRate,
        double durationSeconds,
        string audioSha256Hex,
        string version,
        IReadOnlyList<LevelProfile>? profiles = null,
        string? seedHex = null)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(audioSha256Hex);
        ArgumentNullException.ThrowIfNull(version);

        IReadOnlyList<LevelProfile> levels = profiles ?? ChartGeneratorOptions.DefaultProfiles;
        ChartGeneratorOptions options = new(version, audioSha256Hex, levels)
        {
            SeedMaterialOverrideHex = seedHex,
        };

        // La garde vient avant l'analyse : analyser d'abord pour echouer ensuite
        // rapporterait une erreur de fenetre, alors que la cause est la frequence.
        if (sampleRate <= AnalysisSettings.Nfft)
        {
            throw new ArgumentException(
                $"L'audio est echantillonne a {sampleRate} Hz, et la transformee de Fourier en demande au moins {AnalysisSettings.Nfft}.",
                nameof(sampleRate));
        }

        SpectralAnalysis spectral = SpectralAnalyzer.Analyze(samples, sampleRate);

        // Attention a la division entiere : HopLength est un int, et 512 / 22050
        // vaut zero. Une duree de trame nulle disables tout le suivi de tempo.
        double hopSeconds = (double)AnalysisSettings.HopLength / sampleRate;
        OnsetBands bands = OnsetBands.Compute(
            spectral.Mel, SpectralAnalysis.BandCount, spectral.FrameCount, hopSeconds);

        BeatTrack beats = BeatTracker.Track(
            bands.Values, bands.BandCount, bands.FrameCount, hopSeconds, options.BeatTracker);

        // On ne mesure la phase du temps que si un battement existe : sans
        // battement, la grille est reguliere et la mesure ne dit rien.
        IReadOnlyList<BeatPhase> phases = beats.BeatsSeconds.Count == 0
            ? []
            : BeatTracker.MeasurePhases(spectral.Onset, beats.BeatsSeconds, beats.TempoBpm, hopSeconds);

        KeyCandidate? key = KeyFinder.Find(KeyFinder.Mean(spectral.Chroma, spectral.FrameCount));

        var track = new TrackProfile(sampleRate, durationSeconds, spectral, bands, beats, key);

        var drafts = new List<ChartDraft>(levels.Count);
        foreach (LevelProfile level in levels)
        {
            drafts.Add(ChartGenerator.Generate(track, level, options));
        }

        return new GenerationResult(track, drafts, options);
    }

    /// <summary>Assemble le pack a partir du resultat de generation.</summary>
    /// <param name="result">Le resultat de <see cref="Run"/>.</param>
    /// <param name="identity">Nom, licence et emplacement de l'audio.</param>
    /// <param name="audioBytes">L'audio, tel qu'il sera stocke.</param>
    /// <param name="gapless">Balise gapless lue dans l'audio.</param>
    /// <param name="minPlayerVersion">Version minimale du lecteur.</param>
    public static PackContent BuildPack(
        GenerationResult result,
        PackIdentity identity,
        byte[] audioBytes,
        GaplessTag gapless,
        SemanticVersion minPlayerVersion)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(audioBytes);
        ArgumentNullException.ThrowIfNull(minPlayerVersion);

        TrackProfile track = result.Track;
        string audioSha256 = result.Options.AudioSha256Hex;

        var charts = new List<Chart>(result.Drafts.Count);
        var entries = new List<ChartEntry>(result.Drafts.Count);

        foreach (ChartDraft draft in result.Drafts)
        {
            Chart chart = ChartGenerator.ToChart(
                draft, audioSha256, result.Options.Version, 0, result.Options.SeedMaterialHex);
            charts.Add(chart);
            entries.Add(new ChartEntry
            {
                Level = draft.Level,
                File = PackFormat.ChartFileName(draft.Level),
                NoteCount = chart.NoteCount,
                DurationSeconds = chart.LastNoteTime,
                Nps = Math.Round(PeakNotesPerSecond(draft.Notes), 3, MidpointRounding.AwayFromZero),
                PeakLoad = Math.Round(PeakLoad(draft.Notes), 3, MidpointRounding.AwayFromZero),
                Rating = DifficultyRating(draft),
            });
        }

        Manifest manifest = new()
        {
            Schema = PackFormat.ManifestSchema,
            Id = identity.Id,
            Title = identity.Title,
            Artist = identity.Artist,
            Audio = new AudioRef
            {
                Path = identity.AudioPath,
                Sha256 = audioSha256,
                DurationSeconds = track.DurationSeconds,
                PreSkipSeconds = 0,
                GaplessMetadata = gapless,
            },
            Analysis = new AnalysisInfo
            {
                DurationSeconds = track.DurationSeconds,
                Bpm = Round(track.Beats.TempoBpm, 3),
                BpmConfidence = Round(track.Beats.Confidence, 3),
                Key = track.Key?.Key,
                KeyConfidence = track.Key is null ? null : Round(track.Key.Value.Confidence, 3),
            },
            Generator = new GeneratorInfo
            {
                Name = ChartGenerator.Name,
                Version = result.Options.Version,
                Params = DescribeParameters(result),
                Seed = SeedDerivation.PackSeedValue(
                    result.Options.SeedMaterialHex, result.Options.Version),
            },
            Charts = entries,
            ContentLicense = identity.ContentLicense,
            MinPlayerVersion = minPlayerVersion,
        };

        return new PackContent
        {
            Manifest = manifest,
            Charts = charts,
            AudioPath = identity.AudioPath,
            AudioBytes = audioBytes,
            Report = ReportText(result),
            LicenseText = LicenseText(identity),
        };
    }

    /// <summary>Nombre de notes dans la seconde la plus dense.</summary>
    public static double PeakNotesPerSecond(IReadOnlyList<Note> notes)
    {
        if (notes.Count == 0)
        {
            return 0;
        }

        var counts = new Dictionary<long, int>();
        foreach (Note note in notes)
        {
            long second = (long)Math.Floor(note.Time);
            counts.TryGetValue(second, out int current);
            counts[second] = current + 1;
        }

        int peak = 0;
        foreach (int value in counts.Values)
        {
            peak = Math.Max(peak, value);
        }

        return peak;
    }

    /// <summary>Charge maximale, ramenee dans l'echelle 0-10 du format.</summary>
    /// <remarks>Les poids bruts valent 1, 1,5 et 2 : les diviser par dix est ce
    /// qui les fait tomber naturellement dans l'echelle du manifeste, sans
    /// conversion non documentee (voir la decision D10 du plan).</remarks>
    public static double PeakLoad(IReadOnlyList<Note> notes)
    {
        if (notes.Count == 0)
        {
            return 0;
        }

        double peak = 0;
        int index = 0;
        while (index < notes.Count)
        {
            int end = index;
            while (end < notes.Count && notes[end].Time - notes[index].Time <= 0.1)
            {
                end++;
            }

            double weight = 0;
            int count = 0;
            for (int i = index; i < end; i++)
            {
                weight += WeightOf(notes[i]);
                count++;
            }

            double load = count switch
            {
                0 => 0,
                1 => weight,
                2 => weight * 1.5,
                _ => weight * 2.0,
            };

            peak = Math.Max(peak, load);
            index = end;
        }

        return Math.Round(peak / 10.0, 3, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Fenetre, en secondes, dans laquelle on cherche la rafale la plus dense.
    /// </summary>
    /// <remarks>
    /// C'est la fenetre du <em>terme de difficulte</em>, pas celle de
    /// <see cref="PeakLoad"/> : la charge se lit sur 100 ms parce qu'elle
    /// pese une note a ce moment-la, la rafale se lit sur une seconde
    /// parce qu'elle enchaine les notes. Les confondre donnait un terme
    /// plafonne a 4/32, donc a 3,1 % de la note, alors qu'il en pese 25 %.
    /// La fenetre de 100 ms ne peut pas contenir plus de quatre notes :
    /// la regle de lisibilite L5 l'interdit, et lissage puis simulation
    /// l'appliquent avant le calcul.
    /// </remarks>
    private const double BurstWindowSeconds = 1.0;

    /// <summary>
    /// Nombre de notes en une seconde qui vaut la rafale maximale, donc
    /// terme plein. La regle L5 en autorise quatre en 100 ms ; a l'echelle
    /// d'une seconde cela laisse largement de la place avant la saturation.
    /// </summary>
    private const double BurstReference = 8.0;

    /// <summary>Note de difficulte de 1 a 20 (wiki: generateur.md §9.2).</summary>
    public static int DifficultyRating(ChartDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        if (draft.Notes.Count == 0)
        {
            return 1;
        }

        double averageNps = draft.Notes.Count / Math.Max(0.001, LastTime(draft.Notes));
        double peakNps = Math.Min(1.0, PeakNotesPerSecond(draft.Notes) / 20.0);
        double burst = Math.Min(1.0, LongestBurst(draft.Notes) / BurstReference);
        double strain = Math.Min(1.0, draft.Strain.Peak / 12.0);

        // Les quatre termes pèsent 0,35 + 0,20 + 0,25 + 0,20, soit 1 : la
        // formule est complète et il n'y a pas de cinquième terme oublié. La
        // charge (PeakLoad) n'y figure pas : elle est déjà publiée telle quelle
        // dans le champ « peak_load » du manifeste, où on peut la lire.
        double score = (0.35 * Math.Min(1.0, averageNps / 10.0))
            + (0.20 * peakNps)
            + (0.25 * burst)
            + (0.20 * strain);

        int rating = (int)Math.Round(1 + (score * 19), MidpointRounding.AwayFromZero);
        return Math.Clamp(rating, 1, 20);
    }

    private static double LastTime(IReadOnlyList<Note> notes) => notes[^1].Time;

    private static double WeightOf(Note note) => note.ForceOrDefault / (double)PackFormat.DefaultForce;

    private static int LongestBurst(IReadOnlyList<Note> notes)
    {
        int longest = 0;
        int current = 0;
        double windowStart = 0;
        foreach (Note note in notes)
        {
            if (current == 0 || note.Time - windowStart > BurstWindowSeconds)
            {
                windowStart = note.Time;
                current = 0;
            }

            current++;
            longest = Math.Max(longest, current);
        }

        return longest;
    }

    private static double Round(double value, int digits) =>
        double.IsFinite(value) ? Math.Round(value, digits, MidpointRounding.AwayFromZero) : 0;

    private static JsonObject DescribeParameters(GenerationResult result)
    {
        TrackProfile track = result.Track;
        var parameters = new JsonObject
        {
            ["n_fft"] = AnalysisSettings.Nfft,
            ["hop_length"] = AnalysisSettings.HopLength,
            ["mel_bands"] = AnalysisSettings.MelBands,
            ["chroma_bins"] = AnalysisSettings.ChromaBins,
            ["tuning"] = AnalysisSettings.Tuning,
            ["peak_delta"] = result.Options.PeakPicker.Delta,
            ["prior_bpm"] = result.Options.BeatTracker.PriorBpm,
            ["beat_tightness"] = result.Options.BeatTracker.Tightness,
            ["reaction_seconds"] = result.Options.Player.ReactionSeconds,
            ["jitter_beats"] = result.Options.Humanizer.JitterBeats,
            ["syncope_probability"] = result.Options.Humanizer.SyncopeProbability,
            ["tempo_within_tolerance"] = track.Beats.WithinTolerance,
        };

        // Le materiau n'est note que lorsqu'il a ete impose. Par defaut il
        // vaut l'empreinte de l'audio, deja ecrite ailleurs dans le manifeste :
        // le redire n'ajouterait rien au format.
        if (result.Options.SeedMaterialOverrideHex is string material)
        {
            parameters["seed_material"] = material;
        }

        return parameters;
    }

    private static string ReportText(GenerationResult result)
    {
        var lines = new List<string>
        {
            "Boutap — rapport de generation",
            string.Empty,
            $"Generateur : {ChartGenerator.Name} {result.Options.Version}",
            $"Graine du pack : {result.Options.SeedMaterialHex}",
            string.Empty,
        };

        lines.Add($"Tempo mesure : {result.Track.Beats.TempoBpm:0.000} BPM " +
            $"(confiance {result.Track.Beats.Confidence:0.000}, " +
            $"dans la tolerance de l'a priori : {(result.Track.Beats.WithinTolerance ? "oui" : "non")})");
        lines.Add(result.Track.Key is null
            ? "Tonalite : aucune, le morceau ne se laisse pas classer"
            : $"Tonalite : {result.Track.Key.Value.Key} (correlation {result.Track.Key.Value.Score:0.000})");
        lines.Add(string.Empty);

        foreach (ChartDraft draft in result.Drafts)
        {
            lines.Add($"[{draft.Level.FileNameOf()}] {draft.Notes.Count} note(s), " +
                $"charge maximale {Pipeline.PeakLoad(draft.Notes):0.00}/10, " +
                $"note de difficulte {DifficultyRating(draft)}/20");
            lines.Add($"  {draft.Removed.Count} retiree(s) pour lisibilite, " +
                $"{draft.Unreachable.Count} inatteignable(s)");
            if (draft.Strain.Peak > 0)
            {
                lines.Add($"  effort maximal {draft.Strain.Peak:0.00} a {draft.Strain.PeakTimeSeconds:0.00} s");
            }

            lines.Add(string.Empty);
        }

        return string.Join('\n', lines) + "\n";
    }

    private static string LicenseText(PackIdentity identity) =>
        $"""
         {identity.Title} — contenu distribue sous {identity.ContentLicense}.

         Le code de Boutap est distribue sous AGPL-3.0-or-later ; cette licence ne
         couvre que le contenu de ce pack, pas le jeu.

         Boutap ne-endsors aucune des musiques qu'il convertit, et n'en distribue
         aucune. C'est toi qui detiens les droits sur l'audio que tu as mis ici.
         """.Replace("ne-endsors", "n'endosse");
}
