// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Detection de pics d'onsets : wiki generateur.md §4.6.
//
// Les seuils sont ceux de la specification, exprimes en millisecondes et
// convertis en trames au hop reellement employe. La spec en donne 30, 0, 100,
// 100, 30 et 0,07, et dit explicitement de ne pas les changer sans jeu de
// donnees de reference. C'est donc du code fige, pas un point de reglage.
//
// L'algorithme est celui de Boeck, Krebs et Schedl (ISMIR 2012), le meme que
// librosa : un pic est retenu si, et seulement si,
//   1. il est le maximum de sa fenetre [n - pre_max ; n + post_max] ;
//   2. il depasse la moyenne de [n - pre_avg ; n + post_avg] de plus de delta ;
//   3. il est a plus de wait trames du dernier pic accepte.

using System.Collections.ObjectModel;

namespace Boutap.Gen.Analysis;

/// <summary>Seuils de detection de pics, en secondes, plus le seuil relatif.</summary>
/// <param name="PreMaxSeconds">Longueur de la fenetre de maximum, avant le pic.</param>
/// <param name="PostMaxSeconds">Longueur de la fenetre de maximum, apres le pic.</param>
/// <param name="PreAverageSeconds">Longueur de la fenetre de moyenne, avant le pic.</param>
/// <param name="PostAverageSeconds">Longueur de la fenetre de moyenne, apres le pic.</param>
/// <param name="WaitSeconds">Silence impose entre deux pics acceptes.</param>
/// <param name="Delta">Ecart minimal au-dessus de la moyenne locale, sans unite.</param>
public sealed record PeakPickerOptions(
    double PreMaxSeconds = 0.030,
    double PostMaxSeconds = 0.0,
    double PreAverageSeconds = 0.100,
    double PostAverageSeconds = 0.100,
    double WaitSeconds = 0.030,
    double Delta = 0.07)
{
    /// <summary>Les seuils de la specification, sans modification.</summary>
    public static PeakPickerOptions Specification { get; } = new();
}

/// <summary>Un pic d'onset, en trames et en secondes absolues.</summary>
/// <param name="Frame">Index de la trame dans l'enveloppe.</param>
/// <param name="TimeSeconds">Instant de la trame, en secondes depuis le debut.</param>
/// <param name="Strength">Valeur de l'enveloppe au pic, apres normalisation.</param>
public readonly record struct Onset(int Frame, double TimeSeconds, double Strength);

/// <summary>Selection de pics dans une enveloppe d'onsets normalisee.</summary>
public static class PeakPicker
{
    /// <summary>Plus petit flottant normal positif, le plus petit que l'on puisse ecrire.</summary>
    private const double Tiny = 2.2250738585072014e-308;

    /// <summary>Convertit une duree en nombre de trames, vers le bas.</summary>
    /// <param name="seconds">Duree en secondes.</param>
    /// <param name="sampleRate">Frequence d'echantillonnage, en hertz.</param>
    /// <param name="hopLength">Taille de saut de l'enveloppe, en echantillons.</param>
    /// <returns>Le nombre de trames entier correspondant.</returns>
    public static int Frames(double seconds, double sampleRate, int hopLength)
    {
        if (hopLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(hopLength), hopLength, "Le saut doit etre positif.");
        }

        if (!(sampleRate > 0))
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "La frequence doit etre positive.");
        }

        double frames = Math.Floor(seconds * sampleRate / hopLength);
        return frames > int.MaxValue ? int.MaxValue : (int)frames;
    }

    /// <summary>Retient les pics d'une enveloppe d'onsets.</summary>
    /// <param name="envelope">L'enveloppe, en valeurs relatives au maximum.</param>
    /// <param name="sampleRate">Frequence d'echantillonnage, en hertz.</param>
    /// <param name="hopLength">Taille de saut de l'enveloppe, en echantillons.</param>
    /// <param name="options">Les seuils, ou <see langword="null"/> pour ceux de la spec.</param>
    /// <returns>Les onsets, tries par instant croissant.</returns>
    public static IReadOnlyList<Onset> Pick(
        ReadOnlySpan<double> envelope,
        double sampleRate,
        int hopLength,
        PeakPickerOptions? options = null)
    {
        PeakPickerOptions settings = options ?? PeakPickerOptions.Specification;
        Validate(settings);

        if (envelope.Length == 0)
        {
            return Array.Empty<Onset>();
        }

        double[] scaled = Normalize(envelope);
        bool silent = !scaled.Any(value => value > 0);
        if (silent)
        {
            // Un morceau silencieux n'a pas d'onset. C'est un resultat, pas une panne.
            return Array.Empty<Onset>();
        }

        int preMax = Frames(settings.PreMaxSeconds, sampleRate, hopLength);
        int postMax = Math.Max(1, Frames(settings.PostMaxSeconds, sampleRate, hopLength));
        int preAvg = Frames(settings.PreAverageSeconds, sampleRate, hopLength);
        int postAvg = Math.Max(1, Frames(settings.PostAverageSeconds, sampleRate, hopLength));
        int wait = Frames(settings.WaitSeconds, sampleRate, hopLength);

        List<Onset> found = [];
        int count = scaled.Length;

        // La premiere trame est traitee a part : sa fenetre commence a zero et
        // n'a donc pas de voisin a gauche, ce que la boucle generale ne sait pas
        // exprimer. On l'examine, puis on saute le silence impose.
        int first = 0;
        int firstMax = Math.Min(postMax, count);
        if (IsAtLeast(scaled, 0, firstMax, scaled[0]) && IsAbove(scaled, 0, Math.Min(postAvg, count), scaled[0], settings.Delta))
        {
            found.Add(new Onset(0, 0, scaled[0]));
            first = wait + 1;
        }

        for (int index = Math.Max(1, first); index < count; index++)
        {
            int maxFrom = Math.Max(0, index - preMax);
            int maxTo = Math.Min(index + postMax, count);
            if (!IsAtLeast(scaled, maxFrom, maxTo, scaled[index]))
            {
                continue;
            }

            int avgFrom = Math.Max(0, index - preAvg);
            int avgTo = Math.Min(index + postAvg, count);
            if (!IsAbove(scaled, avgFrom, avgTo, scaled[index], settings.Delta))
            {
                continue;
            }

            found.Add(new Onset(index, index * (double)hopLength / sampleRate, scaled[index]));
            index += wait;
        }

        return new ReadOnlyCollection<Onset>(found);
    }

    private static void Validate(PeakPickerOptions settings)
    {
        if (settings.PreMaxSeconds < 0 || settings.PostMaxSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), "Les fenetres de maximum ne peuvent pas etre negatives.");
        }

        if (settings.PreAverageSeconds < 0 || settings.PostAverageSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), "Les fenetres de moyenne ne peuvent pas etre negatives.");
        }

        if (settings.WaitSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), "Le silence impose ne peut pas etre negatif.");
        }

        if (!(settings.Delta >= 0))
        {
            throw new ArgumentOutOfRangeException(nameof(settings), "L'ecart au-dessus de la moyenne doit etre positif ou nul.");
        }
    }

    /// <summary>Ramene l'enveloppe dans [0 ; 1] en retranchant son minimum.</summary>
    /// <remarks>
    /// Le retranchement du minimum rend le seuil de la specification independant
    /// du niveau general du morceau : un passage muet au debut ne doit pas
    /// decaler le seuil de tout le reste. C'est le meme choix que librosa.
    /// </remarks>
    private static double[] Normalize(ReadOnlySpan<double> envelope)
    {
        double[] scaled = new double[envelope.Length];
        envelope.CopyTo(scaled);
        if (scaled.Length == 0)
        {
            return scaled;
        }

        double minimum = scaled.Min();
        for (int index = 0; index < scaled.Length; index++)
        {
            double value = scaled[index] - minimum;
            scaled[index] = double.IsFinite(value) ? value : 0.0;
        }

        double peak = scaled.Max();
        double divisor = peak + Tiny;
        for (int index = 0; index < scaled.Length; index++)
        {
            scaled[index] /= divisor;
        }

        return scaled;
    }

    private static bool IsAtLeast(double[] values, int from, int to, double candidate)
    {
        for (int index = from; index < to; index++)
        {
            if (values[index] > candidate)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAbove(double[] values, int from, int to, double candidate, double delta)
    {
        if (to <= from)
        {
            return false;
        }

        double total = 0;
        for (int index = from; index < to; index++)
        {
            total += values[index];
        }

        return candidate >= (total / (to - from)) + delta;
    }
}
