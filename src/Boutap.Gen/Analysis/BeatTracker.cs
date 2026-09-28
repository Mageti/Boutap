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
    /// <summary>Plus petit flottant normal positif, comme <c>util.tiny</c>.</summary>
    private const double Tiny = 2.2250738585072014e-308;

    /// <summary>Retrecissement de la cloche de lissage, comme la constante 32 de librosa.</summary>
    /// <remarks>
    /// La cloche du score local vaut <c>exp(-0.5 * (d * 32 / periodFrames)^2)</c>,
    /// donc sa mi-largeur utile vaut un trente-deuxieme de temps.
    /// </remarks>
    private const double Narrowing = 32.0;

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

        // L'enveloppe d'onsets d'Ellis est une seule valeur par trame : le flux
        // bande par bande se moyenne d'abord. C'est ce que font PeakPicker,
        // OnsetEnvelope et librosa, et c'est la seule echelle sur laquelle la
        // penalite de serrage garde le sens que lui donne la specification.
        double[] envelope = Collapse(onsetBands, bandCount, frameCount);
        if (!envelope.Any(value => value > 0))
        {
            return BeatTrack.Empty;
        }

        double[] onsets = NormalizeOnsets(envelope);

        int[] frames = [];
        double[] bestLocalScore = [];
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

            // Le score local depend de l'hypothese : la cloche qui lisse
            // l'enveloppe a pour demi-largeur un temps. On ne peut donc pas le
            // calculer une fois pour toutes avant la boucle, et c'est la que
            // l'ordre des operations change par rapport au reste du projet,
            // ou l'analyse se fait en amont de toute decision.
            double[] localScore = LocalScore(onsets, periodFrames);

            (double[] trial, int[] trialPointer) = DynamicProgram(localScore, periodFrames, settings.Tightness);
            int[] trialFrames = Backtrace(trial, trialPointer);
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
                bestLocalScore = localScore;
                frames = Trim(trialFrames, localScore);
            }
        }

        if (frames.Length < 2)
        {
            return BeatTrack.Empty;
        }

        double span = (frames[^1] - frames[0]) * hopSeconds;
        double tempo = span > 0 ? 60.0 * (frames.Length - 1) / span : 0;
        double confidence = Confidence(bestLocalScore, frames, bestPeriod);
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

    /// <summary>Ramene le flux bande par bande a une valeur par trame.</summary>
    /// <param name="onsetBands">Le flux, en bande par bande, en trames.</param>
    /// <param name="bandCount">Nombre de bandes mel.</param>
    /// <param name="frameCount">Nombre de trames.</param>
    /// <returns>La moyenne des bandes, pour chaque trame.</returns>
    private static double[] Collapse(ReadOnlySpan<double> onsetBands, int bandCount, int frameCount)
    {
        var envelope = new double[frameCount];
        for (int frame = 0; frame < frameCount; frame++)
        {
            int offset = frame * bandCount;
            double total = 0;
            for (int band = 0; band < bandCount; band++)
            {
                total += onsetBands[offset + band];
            }

            envelope[frame] = total / bandCount;
        }

        return envelope;
    }

    /// <summary>Ramene l'enveloppe a une echelle sans unite.</summary>
    /// <param name="envelope">L'enveloppe d'onsets, en decibels.</param>
    /// <returns>L'enveloppe divisee par son ecart-type.</returns>
    /// <remarks>
    /// C'est <c>__normalize_onsets</c> de librosa, et c'est l'etape sans
    /// laquelle la penalite de serrage n'a aucun sens : une enveloppe en
    /// decibels monte a soixante-dix, une penalite d'un demi-temps vaut
    /// quarante-huit, et le DP prefere alors systematiquement la grille la
    /// plus lente du morceau, puisque c'est celle qui paie le moins de
    /// penalites. Diviser par l'ecart-type rend les deux grandeurs
    /// comparables, et le tempo redevient une mesure.
    /// </remarks>
    private static double[] NormalizeOnsets(double[] envelope)
    {
        int count = envelope.Length;
        var normalized = new double[count];
        if (count < 2)
        {
            Array.Copy(envelope, normalized, count);
            return normalized;
        }

        double mean = 0;
        foreach (double value in envelope)
        {
            mean += value;
        }

        mean /= count;

        double sumSquares = 0;
        foreach (double value in envelope)
        {
            double centred = value - mean;
            sumSquares += centred * centred;
        }

        // Ecart-type d'echantillon, comme le ddof = 1 de numpy.
        double deviation = Math.Sqrt(sumSquares / (count - 1));
        double divisor = deviation + Tiny;
        for (int index = 0; index < count; index++)
        {
            normalized[index] = envelope[index] / divisor;
        }

        return normalized;
    }

    /// <summary>Score local d'Ellis : contraste local a l'echelle d'un temps.</summary>
    /// <param name="onsets">L'enveloppe d'onsets, deja normalisee.</param>
    /// <param name="periodFrames">Periode de l'hypothese de tempo, en trames.</param>
    /// <returns>Le score local, une valeur par trame.</returns>
    /// <remarks>
    /// <para>
    /// C'est <c>__beat_local_score</c> de librosa : une convolution de mode
    /// « same » par une cloche gaussienne centree sur la trame et large d'un
    /// temps, <c>exp(-0.5 * (d * 32 / periodFrames)^2)</c> pour un retard
    /// <c>d</c>. Comme dans librosa, la fenetre ne regarde que le passe, et
    /// comme dans librosa elle est recalculee pour chaque hypothese de tempo :
    /// un temps a 60 BPM et un temps a 200 BPM ne se ressemblent pas.
    /// </para>
    /// <para>
    /// La specification (generateur.md 4.7.2) demandait
    /// <c>sum(bandes - moyenne(onsets[:, i]))</c>, en presentant cela comme « la
    /// somme de ce qui depasse le fond ». Cette somme vaut exactement zero :
    /// on soustrait a chaque bande la moyenne de ces memes bandes. Ce n'est ni
    /// ce que fait Ellis, ni ce que fait librosa, et la formule est remplacee
    /// ici par le score local reel. La divergence est consignee dans le wiki.
    /// </para>
    /// </remarks>
    private static double[] LocalScore(double[] onsets, double periodFrames)
    {
        int count = onsets.Length;
        var scores = new double[count];
        int reach = Math.Min((int)Math.Ceiling(periodFrames), count - 1);
        if (reach < 0)
        {
            return scores;
        }

        var bell = new double[reach + 1];
        for (int lag = 0; lag <= reach; lag++)
        {
            double scaled = lag * Narrowing / periodFrames;
            bell[lag] = Math.Exp(-0.5 * scaled * scaled);
        }

        for (int frame = 0; frame < count; frame++)
        {
            double total = 0;
            int last = Math.Min(reach, frame);
            for (int lag = 0; lag <= last; lag++)
            {
                total += bell[lag] * onsets[frame - lag];
            }

            scores[frame] = total;
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
        int end = LastBeat(cumulative);
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

    /// <summary>Derniere trame du chemin, en ecarte les battements de queue.</summary>
    /// <param name="cumulative">Le score cumule du DP, une valeur par trame.</param>
    /// <returns>L'index de depart de la remontee.</returns>
    /// <remarks>
    /// C'est <c>__last_beat</c> de librosa, qui existe pour une raison
    /// precisee : une piste qui finit exactement sur la derniere trame compte
    /// un battement qui n'existe pas. Ce battement de queue etire l'ecart entre
    /// le premier et le dernier temps, et le tempo final, qui vaut
    /// <c>60 * (n - 1) / etendue</c>, en sort rabaisse d'autant. Sur un morceau
    /// de 120 BPM dont la derniere note tombe a 19,5 s dans un fichier de 20 s,
    /// cela donnait 117 BPM au lieu de 120 : le tempo n'etait pas mesure de
    /// travers, il etait gonfle par une trame.
    /// </remarks>
    private static int LastBeat(double[] cumulative)
    {
        int count = cumulative.Length;
        if (count < 2)
        {
            return Math.Max(0, count - 1);
        }

        bool[] isPeak = new bool[count];
        for (int index = 1; index + 1 < count; index++)
        {
            isPeak[index] = cumulative[index] > cumulative[index - 1] && cumulative[index] >= cumulative[index + 1];
        }

        isPeak[count - 1] = cumulative[count - 1] > cumulative[count - 2];

        // La mediane se prend sur ce qui n'est PAS un maximum local : les pics
        // sont par definition au-dessus, ils servent pas de niveau du bruit.
        List<double> others = new(count);
        for (int index = 0; index < count; index++)
        {
            if (!isPeak[index])
            {
                others.Add(cumulative[index]);
            }
        }

        double threshold = 0.5 * Median(others);
        for (int index = count - 1; index >= 0; index--)
        {
            if (isPeak[index] && cumulative[index] >= threshold)
            {
                return index;
            }
        }

        return count - 1;
    }

    /// <summary>Ecarte les battements de tete et de queue, sans signal.</summary>
    /// <param name="frames">Les battements du chemin, en ordre croissant.</param>
    /// <param name="localScore">Le score local de l'hypothese gagnante.</param>
    /// <returns>Les battements qui portent vraiment un depart.</returns>
    /// <remarks>
    /// C'est <c>__trim_beats</c> de librosa, qui existe parce que la
    /// remontee part toujours de la trame zero et s'arrete sur le dernier
    /// maximum local du score cumule : ces deux extremites ne sont pas
    /// forcément des departs. Sans cette coupe, un fichier dont la premiere
    /// note tombe a 0,28 s se voit ajouter un battement a zero, et la piste
    /// gagne une periode entiere. Mesure sur le morceau de reference, la
    /// grille passe de 40 battements mal places a 38, exactement ceux que
    /// librosa retrouve sur le meme fichier, et le tempo de 118,4 a 119,96 BPM,
    /// soit la vitesse reelle du morceau a un centieme pres.
    /// </remarks>
    private static int[] Trim(int[] frames, double[] localScore)
    {
        if (frames.Length == 0)
        {
            return frames;
        }

        // Lissage de hanning(5) : 0, 0,5, 1, 0,5, 0, comme librosa, puis
        // demi-quart de la racine moyenne des carres. Le seuil compare chaque
        // battement a l'energie qu'il devrait porter.
        var smoothed = new double[frames.Length];
        double total = 0;
        for (int index = 0; index < frames.Length; index++)
        {
            double left = index > 0 ? localScore[frames[index - 1]] : 0.0;
            double right = index + 1 < frames.Length ? localScore[frames[index + 1]] : 0.0;
            double here = localScore[frames[index]];
            double value = (0.5 * left) + here + (0.5 * right);
            smoothed[index] = value;
            total += value * value;
        }

        double threshold = 0.5 * Math.Sqrt(total / smoothed.Length);

        int from = 0;
        while (from < frames.Length && localScore[frames[from]] <= threshold)
        {
            from++;
        }

        int to = frames.Length - 1;
        while (to >= from && localScore[frames[to]] <= threshold)
        {
            to--;
        }

        return to < from ? [] : frames[from..(to + 1)];
    }

    /// <summary>Mediane, comme <c>numpy.median</c> : moyenne des deux du milieu si pair.</summary>
    private static double Median(List<double> values)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        values.Sort();
        int middle = values.Count / 2;
        return values.Count % 2 == 1 ? values[middle] : (values[middle - 1] + values[middle]) / 2.0;
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
