// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Suivi de battement : wiki generateur.md §4.7 et §4.9.
//
// L'algorithme est celui de Daniel P. W. Ellis, "Beat Tracking by Dynamic
// Programming", Journal of Music Research 36(1):51-60, 2007.
//
// Il ne s'agit pas de mesurer un tempo : le tempo d'un morceau n'est pas une
// donnee, c'est une hypothese. On en teste une famille entiere et on garde
// celle qui explique le mieux les onsets, puis on assume l'incertitude.

using System.Collections.ObjectModel;

namespace Boutap.Gen.Analysis;

/// <summary>Reglages du suivi de battement.</summary>
/// <param name="PriorBpm">Tempo a priori, centre de la cloche de ponderation.</param>
/// <param name="MinimumBpm">Borne basse de la recherche.</param>
/// <param name="MaximumBpm">Borne haute de la recherche.</param>
/// <param name="Tightness">Poids de la penalite de deviation de periodicite.</param>
/// <param name="ToleranceFraction">Ecart admis entre le tempo trouve et l'a priori.</param>
public sealed record BeatTrackerOptions(
    double PriorBpm = 120.0,
    double MinimumBpm = 40.0,
    double MaximumBpm = 200.0,
    double Tightness = 100.0,
    double ToleranceFraction = 0.10)
{
    /// <summary>Les reglages de la specification, sans modification.</summary>
    public static BeatTrackerOptions Specification { get; } = new();
}

/// <summary>Ce que le suivi de battement a conclu.</summary>
/// <param name="TempoBpm">Tempo mesure, en battements par minute.</param>
/// <param name="BeatsSeconds">Les instants de battement, en secondes absolues.</param>
/// <param name="Confidence">Part du flux d'onsets expliquee, entre 0 et 1.</param>
/// <param name="WithinTolerance">
/// <see langword="true"/> si le tempo tombe dans la plage admise autour de
/// l'a priori. Un tempo hors plage n'est pas une erreur : c'est un morceau
/// comptique ou un tempo inhabituel, et il faut le dire plutot que de le
/// corriger en silence.
/// </param>
public sealed record BeatTrack(
    double TempoBpm,
    IReadOnlyList<double> BeatsSeconds,
    double Confidence,
    bool WithinTolerance)
{
    /// <summary>Une piste vide, rendue quand le signal ne permet rien.</summary>
    public static BeatTrack Empty { get; } = new(0, Array.Empty<double>(), 0, false);
}

/// <summary>Suivi de battement par programmation dynamique.</summary>
public static class BeatTracker
{
    /// <summary>Finesse de la grille de tempos testee, en battements par minute.</summary>
    private const double TempoStepBpm = 1.0;

    /// <summary>Etapes possibles du battement, une par phase du temps.</summary>
    /// <param name="Subdivisions">Nombre de phases echantillonnees dans le temps.</param>
    public const int Subdivisions = 16;

    /// <summary>Suit la grille de battement d'un flux d'onsets par bande.</summary>
    /// <param name="onsetBands">Le flux, en bande par bande, en trames.</param>
    /// <param name="bandCount">Nombre de bandes mel.</param>
    /// <param name="frameCount">Nombre de trames.</param>
    /// <param name="hopSeconds">Duree d'une trame, en secondes.</param>
    /// <param name="options">Les reglages, ou <see langword="null"/> pour ceux de la spec.</param>
    /// <returns>La piste suivie, ou une piste vide si le signal est muet.</returns>
    public static BeatTrack Track(
        ReadOnlySpan<double> onsetBands,
        int bandCount,
        int frameCount,
        double hopSeconds,
        BeatTrackerOptions? options = null)
    {
        BeatTrackerOptions settings = options ?? BeatTrackerOptions.Specification;
        Validate(settings, onsetBands.Length, bandCount, frameCount, hopSeconds);

        if (frameCount < 2)
        {
            return BeatTrack.Empty;
        }

        double[] localScore = LocalScores(onsetBands, bandCount, frameCount);
        if (!localScore.Any(value => value > 0))
        {
            return BeatTrack.Empty;
        }

        int[] frames = [];
        double bestScore = double.NegativeInfinity;
        double bestPeriod = 0;

        // Ellis ne choisit pas un tempo puis ne cherche plus : il programme
        // dynamiquement sur CHAQUE hypothese, et une cloche d'une octave autour
        // de l'apriori arbitre entre elles. C'est elle qui departage 60 et 120
        // BPM, indiscernables sur le flux seul.
        //
        // Comparer les scores cumules d'un tempo a l'autre serait faux si la
        // penalite de serrage ne s'annulait pas a la periode exacte : une grille
        // a 40 BPM n'en payerait que quarante contre cent vingt a 120, et la
        // plus lente gagnerait toujours. Elle s'annule, donc on compare
        // directement l'energie d'onsets ramenee par la grille, et la ponderation
        // d'octave tranche les cas ambigus.
        for (double bpm = settings.MinimumBpm; bpm <= settings.MaximumBpm; bpm += TempoStepBpm)
        {
            double periodFrames = 60.0 / bpm / hopSeconds;
            if (periodFrames < 2)
            {
                continue;
            }

            (double[] trial, int[] trialPointer) = DynamicProgram(localScore, periodFrames, settings.Tightness);
            int[] trialFrames = Backtrace(trial, trialPointer);
            if (trialFrames.Length < 2)
            {
                continue;
            }

            double captured = 0;
            foreach (int frame in trialFrames)
            {
                if (localScore[frame] > 0)
                {
                    captured += localScore[frame];
                }
            }

            double score = captured * Weight(periodFrames, 60.0 / settings.PriorBpm / hopSeconds);
            if (score > bestScore)
            {
                bestScore = score;
                bestPeriod = periodFrames;
                frames = trialFrames;
            }
        }

        if (frames.Length < 2)
        {
            return BeatTrack.Empty;
        }

        double span = (frames[^1] - frames[0]) * hopSeconds;
        double tempo = span > 0 ? 60.0 * (frames.Length - 1) / span : 0;
        double confidence = Confidence(localScore, frames, bestPeriod);
        bool within = Math.Abs(tempo - settings.PriorBpm) <= settings.ToleranceFraction * settings.PriorBpm;

        List<double> beats = new(frames.Length);
        foreach (int frame in frames)
        {
            beats.Add(frame * hopSeconds);
        }

        return new BeatTrack(tempo, new ReadOnlyCollection<double>(beats), confidence, within);
    }

    /// <summary>Energie des phases d'un battement, pour choisir la quantification.</summary>
    /// <param name="envelope">Une enveloppe de force, en trames.</param>
    /// <param name="beatSeconds">Les instants de battement.</param>
    /// <param name="tempoBpm">Le tempo, en battements par minute.</param>
    /// <param name="hopSeconds">Duree d'une trame, en secondes.</param>
    /// <param name="phases">Phases echantillonnees par battement.</param>
    /// <returns>
    /// Pour chaque battement, l'energie de chacune des phases, puis la phase la
    /// plus forte. Vides si la piste ne contient aucun battement.
    /// </returns>
    public static IReadOnlyList<BeatPhase> MeasurePhases(
        ReadOnlySpan<double> envelope,
        IReadOnlyList<double> beatSeconds,
        double tempoBpm,
        double hopSeconds,
        int phases = Subdivisions)
    {
        if (phases < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(phases), phases, "Il faut au moins une phase.");
        }

        List<BeatPhase> measured = new(beatSeconds.Count);
        if (beatSeconds.Count == 0 || !(tempoBpm > 0))
        {
            return new ReadOnlyCollection<BeatPhase>(measured);
        }

        double beatSecondsLength = 60.0 / tempoBpm;
        for (int index = 0; index < beatSeconds.Count; index++)
        {
            double start = beatSeconds[index];
            double next = index + 1 < beatSeconds.Count ? beatSeconds[index + 1] : start + beatSecondsLength;
            double[] energies = new double[phases];
            for (int phase = 0; phase < phases; phase++)
            {
                double from = start + (next - start) * phase / phases;
                double to = start + (next - start) * (phase + 1) / phases;
                energies[phase] = Total(envelope, from, to);
            }

            int strongest = 0;
            for (int phase = 1; phase < phases; phase++)
            {
                if (energies[phase] > energies[strongest])
                {
                    strongest = phase;
                }
            }

            measured.Add(new BeatPhase(start, next - start, energies, strongest));
        }

        return new ReadOnlyCollection<BeatPhase>(measured);
    }

    private static void Validate(
        BeatTrackerOptions settings,
        int length,
        int bandCount,
        int frameCount,
        double hopSeconds)
    {
        if (length < bandCount * frameCount)
        {
            throw new ArgumentException(
                $"Le flux contient {length} valeurs, moins que {bandCount} bandes x {frameCount} trames.",
                nameof(length));
        }

        if (!(hopSeconds > 0))
        {
            throw new ArgumentOutOfRangeException(nameof(hopSeconds), hopSeconds, "La duree de trame doit etre positive.");
        }

        if (!(settings.MinimumBpm > 0) || !(settings.MaximumBpm > settings.MinimumBpm))
        {
            throw new ArgumentOutOfRangeException(nameof(settings), "La plage de recherche est vide ou negative.");
        }

        if (!(settings.PriorBpm > 0))
        {
            throw new ArgumentOutOfRangeException(nameof(settings), "Le tempo a priori doit etre positif.");
        }
    }

    /// <summary>Part du flux d'onsets qui tombe dans une fenetre de battement.</summary>
    /// <remarks>
    /// On ne peut pas comparer le score cumule a la somme de toutes les trames :
    /// la piste ne passe que par une poignee de frames, le rapport serait donc
    /// petit meme sur un tempo parfaitement trouve. Ce qui compte, c'est la
    /// proportion d'energie d'onsets que la grille couvre reellement.
    /// </remarks>
    private static double Confidence(double[] localScore, int[] frames, double periodFrames)
    {
        double total = 0;
        for (int frame = 0; frame < localScore.Length; frame++)
        {
            if (localScore[frame] > 0)
            {
                total += localScore[frame];
            }
        }

        if (!(total > 0) || frames.Length == 0)
        {
            return 0;
        }

        int window = Math.Max(1, (int)Math.Round(periodFrames / 4.0));
        bool[] covered = new bool[localScore.Length];
        foreach (int frame in frames)
        {
            int from = Math.Max(0, frame - window);
            int to = Math.Min(localScore.Length - 1, frame + window);
            for (int index = from; index <= to; index++)
            {
                covered[index] = true;
            }
        }

        double captured = 0;
        for (int frame = 0; frame < localScore.Length; frame++)
        {
            if (covered[frame] && localScore[frame] > 0)
            {
                captured += localScore[frame];
            }
        }

        return Math.Clamp(captured / total, 0, 1);
    }

    /// <summary>Score local d'Ellis : le flux retire de sa moyenne de trame.</summary>
    private static double[] LocalScores(ReadOnlySpan<double> onsetBands, int bandCount, int frameCount)
    {
        double[] scores = new double[frameCount];
        for (int frame = 0; frame < frameCount; frame++)
        {
            int offset = frame * bandCount;
            double total = 0;
            for (int band = 0; band < bandCount; band++)
            {
                total += onsetBands[offset + band];
            }

            double mean = total / bandCount;
            double centred = 0;
            for (int band = 0; band < bandCount; band++)
            {
                centred += onsetBands[offset + band] - mean;
            }

            scores[frame] = centred;
        }

        // Ellis travaille sur une enveloppe d'onsets normalisee, ou la serrage
        // vaille quelques centaines de points. Sans cette normalisation, la
        // moindre note reelle d'un morceau，vaut dix milliemes, et la
        // penalite de periodicite/ecrase tout : le DP renvoie alors la grille la
        // plus lent du morceau, parce que c'est celle qui paie le moins de
        // penalites. Le tempo devient une division, pas une mesure.
        double peak = 0;
        foreach (double score in scores)
        {
            if (score > peak)
            {
                peak = score;
            }
        }

        if (peak > 0)
        {
            for (int frame = 0; frame < scores.Length; frame++)
            {
                scores[frame] /= peak;
            }
        }

        return scores;
    }

    /// <summary>Cloche d'une octave autour de l'apriori, en octaves de tempo.</summary>
    /// <param name="periodFrames">Periode de l'hypothese, en trames.</param>
    /// <param name="priorPeriodFrames">Periode de l'apriori, en trames.</param>
    private static double Weight(double periodFrames, double priorPeriodFrames)
    {
        if (!(periodFrames > 0) || !(priorPeriodFrames > 0))
        {
            return 0;
        }

        double octaves = Math.Log2(periodFrames / priorPeriodFrames);
        return Math.Exp(-0.5 * octaves * octaves);
    }

    /// <summary>Programmation dynamique d'Ellis, avec remontee du chemin.</summary>
    private static (double[] Cumulative, int[] Pointer) DynamicProgram(
        double[] localScore,
        double reference,
        double tightness)
    {
        int count = localScore.Length;
        double[] cumulative = new double[count];
        int[] pointer = new int[count];
        pointer[0] = 0;

        // La recherche porte sur l'ecart entre deux battements, en trames, et
        // sur rien d'autre. C'est le facteur 12 qui apparait dans la seule
        // penalite de serrage, ou il compte douze doubles-croches par temps.
        // L'appliquer aussi a la fenetre revient a chercher un precedent entre
        // 3 et 12 secondes : on trouve alors un tempo dix fois trop lent, sans
        // jamais s'en apercevoir.
        int window = Math.Max(2, (int)Math.Round(reference));
        int from = Math.Max(2, 2 * window);

        for (int frame = from; frame < count; frame++)
        {
            int lowest = Math.Max(0, frame - (2 * window));
            int highest = frame - (window / 2);
            if (highest < lowest)
            {
                highest = lowest;
            }

            double best = double.NegativeInfinity;
            int bestIndex = Math.Max(0, frame - from);
            for (int previous = lowest; previous <= highest && previous < frame; previous++)
            {
                // La penalite vaut 0 pour un battement a la periode exacte de
                // l'hypothese, et 48 points a un demi-temps comme a deux temps
                // (ln(0,5)^2 = ln(2)^2 = 0,48). C'est exactement ce qu'annonce
                // la specification. Le facteur 12 qu'Ellis divise vient de sa
                // grille de tempos surechantillonnee ; le conserver ici rendait
                // la grille la plus LENTE toujours moins chere, puisque
                // ln(lag/12) est plus petit pour un ecart double que pour un
                // ecart simple, et le tempo remontait divise par deux.
                double lag = (frame - previous) / reference;
                if (lag <= 0)
                {
                    continue;
                }

                double penalty = tightness * Math.Log(lag) * Math.Log(lag);
                double candidate = cumulative[previous] - penalty;
                if (candidate > best)
                {
                    best = candidate;
                    bestIndex = previous;
                }
            }

            cumulative[frame] = best + localScore[frame];
            pointer[frame] = bestIndex;
        }

        return (cumulative, pointer);
    }

    private static int[] Backtrace(double[] cumulative, int[] pointer)
    {
        int end = cumulative.Length - 1;
        int best = 0;
        for (int frame = 1; frame < cumulative.Length; frame++)
        {
            if (cumulative[frame] > cumulative[best])
            {
                best = frame;
            }
        }

        List<int> frames = [];
        int cursor = end;
        int guard = 0;
        while (cursor > 0 && guard++ <= cumulative.Length)
        {
            frames.Add(cursor);
            cursor = pointer[cursor];
        }

        if (cursor == 0 && frames.Count > 0)
        {
            frames.Add(0);
        }

        frames.Reverse();
        return frames.ToArray();
    }

    private static double Total(ReadOnlySpan<double> envelope, double from, double to)
    {
        int first = Math.Max(0, (int)Math.Floor(from));
        int last = Math.Min(envelope.Length, (int)Math.Ceiling(to));
        double total = 0;
        for (int index = first; index < last; index++)
        {
            total += envelope[index];
        }

        return total;
    }
}
