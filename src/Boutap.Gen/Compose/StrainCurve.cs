// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Courbe de strain et note de difficulte : wiki generateur.md §9.1 et §9.2.
//
// Le modele est celui de l'analyse de danse des jeux de rythme. Une note
// ajoute un effort, la main relaxe ensuite exponentiellement. Un SRC eleve et
// soutenu est un morceau « lourd » ; un SRC pic mais court est un moment
// intense mais jouable. Ce n'est pas la meme chose, et la distinction compte
// pour l'accessibilite : un joueur peut accepter le premier et pas le second.

using System.Collections.ObjectModel;

using Boutap.Core.Pack;

namespace Boutap.Gen.Compose;

/// <summary>Les quatre efforts du modele de strain.</summary>
public static class StrainEffort
{
    /// <summary>Une note frappee.</summary>
    public const double Tap = 1.0;

    /// <summary>Une note tenue : la main reste posee, le coup coute plus cher.</summary>
    public const double Hold = 1.3;

    /// <summary>Une note d'accord : deux doigts, un mouvement.</summary>
    public const double Chord = 2.0;

    /// <summary>Un jack : deux touches, un aller-retour, l'effort maximal.</summary>
    /// <remarks>
    /// Le format de la V1 n'a aucun moyen de distinguer une main, donc aucun
    /// moyen de representer un jack. La constante reste lacite pour que la
    /// formule de la specification soit ecrite en entier, et pour qu'un futur
    /// format de note la rende disponible sans qu'on ait a la redécouvrir.
    /// </remarks>
    public const double Jack = 4.0;
}

/// <summary>Le profil de la courbe de strain.</summary>
public sealed record StrainProfile(
    IReadOnlyList<double> TimesSeconds,
    IReadOnlyList<double> Values,
    double Peak,
    double PeakTimeSeconds)
{
    /// <summary>Un profil vide, rendu quand la partition ne contient aucune note.</summary>
    public static StrainProfile Empty { get; } = new(
        Array.Empty<double>(),
        Array.Empty<double>(),
        0,
        0);
}

/// <summary>Le calcul de la courbe de strain.</summary>
public static class StrainCurve
{
    /// <summary>Constante de temps de la relaxation, en secondes.</summary>
    /// <remarks>
    /// Une main qui vient de frapper ne recupere pas instantanement : elle met
    /// environ 1,2 seconde a redevenir totalement disponible.
    /// </remarks>
    public const double ReleaseSeconds = 1.2;

    /// <summary>Le pas d'echantillonnage de la courbe, en secondes.</summary>
    /// <remarks>
    /// Un dixieme de seconde. La constante de relaxation est de 1,2 s, donc un
    /// pas dix fois plus petit n'apporte rien : on ne calcule pas plus finely
    /// que ce qu'on sait mesurer.
    /// </remarks>
    public const double StepSeconds = 0.1;

    /// <summary>Calcule la courbe de strain d'une partition.</summary>
    /// <param name="notes">Les notes, triees par instant croissant.</param>
    /// <returns>La courbe, echantillonnee, et son maximum.</returns>
    public static StrainProfile Compute(IReadOnlyList<Note> notes)
    {
        ArgumentNullException.ThrowIfNull(notes);
        if (notes.Count == 0)
        {
            return StrainProfile.Empty;
        }

        double start = notes.Min(note => note.Time);
        double end = notes.Max(note => note.EndTime) + ReleaseSeconds;
        int count = Math.Max(1, (int)Math.Ceiling((end - start) / StepSeconds) + 1);

        List<double> times = new(count);

        // Une capacite n'est pas une longueur : sans ces remplissages, values
        // serait vide et la premiere ecriture tomberait hors du tableau.
        var values = new double[count];
        for (int step = 0; step < count; step++)
        {
            times.Add(start + (step * StepSeconds));
        }

        foreach (Note note in notes)
        {
            double effort = Effort(note);
            for (int step = 0; step < count; step++)
            {
                double elapsed = times[step] - note.Time;
                if (elapsed < 0)
                {
                    continue;
                }

                values[step] += effort * Math.Exp(-elapsed / ReleaseSeconds);
            }
        }

        double peak = 0;
        int peakIndex = 0;
        for (int step = 0; step < count; step++)
        {
            if (values[step] > peak)
            {
                peak = values[step];
                peakIndex = step;
            }
        }

        return new StrainProfile(
            new ReadOnlyCollection<double>(times),
            new ReadOnlyCollection<double>([.. values]),
            peak,
            times[peakIndex]);
    }

    /// <summary>L'effort instantane d'une note.</summary>
    /// <param name="note">La note, avec ses voisines si le contexte compte.</param>
    /// <returns>L'effort, entre 1 et 4.</returns>
    public static double Effort(Note note)
    {
        if (note.IsHold)
        {
            return StrainEffort.Hold;
        }

        return StrainEffort.Tap;
    }

    /// <summary>L'effort d'un groupe de notes frappees ensemble.</summary>
    /// <param name="notes">Les notes, dont les premieres peuvent etre simultanees.</param>
    /// <param name="count">Le nombre de notes du groupe.</param>
    /// <returns>Un effort simple, un accord, ou le maximum concevable.</returns>
    /// <remarks>
    /// Deux notes simultanees valent un accord. Un jack, lui, se reconnait a ce
    /// qu'il est sur deux touches voisines dans un temps court, ce que la V1 ne
    /// sait pas representer : on rend alors l'effort d'un accord plutot que
    /// d'inventer un jack.
    /// </remarks>
    public static double GroupEffort(IReadOnlyList<Note> notes, int count)
    {
        if (count <= 0)
        {
            return 0;
        }

        if (count == 1)
        {
            return Effort(notes[0]);
        }

        return count == 2 ? StrainEffort.Chord : StrainEffort.Jack;
    }
}
