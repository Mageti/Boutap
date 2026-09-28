// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Reconnaissance de tonalite : wiki generateur.md §4.8.
//
// Gabarits de Krumhansl-Schmuckler, edition Krumhansl et Kessler (1982),
// "A Generative Theory of Tonal Music", Journal of the American Statistical
// Association 77(358):45-50.
//
// Les deux gabarits sont ceux de 1982, complets et sans retouche. La
// specification en recopie six valeurs, mais ses etiquettes sont decalees :
// elle place 4.38 sur la mediance et 5.38 sur la dominante, alors que 4.38 est
// la dominante de 1982, que 5.38 est la sous-dominante du gabarit mineur, et
// que le 3.48 qu'elle nomme « sixte » est bien la mediante de 1982. Suivre la
// specification menait donc a une tonalite fausse, La majeur etant attribuee a
// Re bemol. La divergence est consignee dans le wiki.

using System.Collections.ObjectModel;

using Boutap.Core.Common;

namespace Boutap.Gen.Analysis;

/// <summary>Une tonalite candidate, avec le score qui la distingue des autres.</summary>
/// <param name="Key">La tonalite trouvee.</param>
/// <param name="Score">Correlation de Pearson avec son gabarit.</param>
/// <param name="Margin">Ecart avec la deuxieme meilleure candidate.</param>
public readonly record struct KeyCandidate(KeySignature Key, double Score, double Margin)
{
    /// <summary>Une confiance dans [0 ; 1], derivée de l'ecart avec la deuxieme.</summary>
    /// <remarks>
    /// La correlation seule ne dit rien de l'excellence de la reponse : un
    /// chroma uniformement plat donne 0,0 a toutes les tonalites, et le
    /// maximum y est aussi inexistant. L'ecart avec la deuxieme, lui, distingue
    /// « aucune tonalite dominante » de « une tonalite qui l'emporte ».
    /// </remarks>
    public double Confidence => Math.Clamp(Margin, 0, 1);
}

/// <summary>Recherche de la tonalite dominante d'un chroma moyen.</summary>
public static class KeyFinder
{
    /// <summary>Gabarit majeur de 1982, en demi-tons depuis la tonique.</summary>
    /// <remarks>
    /// do, re, mi, fa, sol, la, si, do, re, mi, fa, sol.
    /// </remarks>
    private static readonly double[] MajorProfile =
    [
        6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 5.19, 2.39, 3.66, 2.29, 2.88,
    ];

    /// <summary>Gabarit mineur de 1982, en demi-tons depuis la tonique.</summary>
    private static readonly double[] MinorProfile =
    [
        6.33, 2.68, 3.52, 5.38, 2.60, 3.53, 2.54, 4.75, 3.98, 2.69, 3.34, 3.17,
    ];

    /// <summary>Les vingt-quatre candidats, par ordre de score decroissant.</summary>
    /// <param name="chroma">Le chroma moyen du morceau, sur douze classes.</param>
    /// <returns>Tous les candidats tries, le meilleur en tete.</returns>
    public static IReadOnlyList<KeyCandidate> Rank(ReadOnlySpan<double> chroma)
    {
        if (chroma.Length != MajorProfile.Length)
        {
            throw new ArgumentException(
                $"Le chroma doit compter {MajorProfile.Length} classes, il en compte {chroma.Length}.",
                nameof(chroma));
        }

        List<KeyCandidate> candidates = new(2 * MajorProfile.Length);
        for (int tonic = 0; tonic < MajorProfile.Length; tonic++)
        {
            Add(candidates, tonic, KeyMode.Major, MajorProfile, chroma);
            Add(candidates, tonic, KeyMode.Minor, MinorProfile, chroma);
        }

        candidates.Sort(static (left, right) => right.Score.CompareTo(left.Score));
        for (int index = 0; index < candidates.Count; index++)
        {
            double margin = index + 1 < candidates.Count ? candidates[index].Score - candidates[index + 1].Score : candidates[index].Score;
            candidates[index] = candidates[index] with { Margin = margin };
        }

        return new ReadOnlyCollection<KeyCandidate>(candidates);
    }

    /// <summary>La tonalite dominante, ou une absence de reponse explicite.</summary>
    /// <param name="chroma">Le chroma moyen du morceau, sur douze classes.</param>
    /// <param name="minimumCorrelation">
    /// En dessous de ce niveau de correlation, aucun gabarit ne decrit le
    /// morceau : c'est du bruit, un climax percussif, ou une modulation qu'aucune
    /// tonique unique ne resume. Rendre une reponse quand meme serait mentir.
    /// </param>
    /// <returns>Le meilleur candidat, ou <see langword="null"/> si rien ne se degage.</returns>
    public static KeyCandidate? Find(ReadOnlySpan<double> chroma, double minimumCorrelation = 0.15)
    {
        IReadOnlyList<KeyCandidate> ranked = Rank(chroma);
        if (ranked.Count == 0 || ranked[0].Score < minimumCorrelation)
        {
            return null;
        }

        return ranked[0];
    }

    /// <summary>Chroma moyen d'un chroma par trame.</summary>
    /// <param name="chroma">Le chroma, en trame par trame, sur douze classes.</param>
    /// <param name="frameCount">Nombre de trames.</param>
    /// <returns>La moyenne par classe, sur douze valeurs.</returns>
    public static double[] Mean(ReadOnlySpan<double> chroma, int frameCount)
    {
        if (frameCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frameCount), frameCount, "Le nombre de trames ne peut pas etre negatif.");
        }

        if (chroma.Length < frameCount * MajorProfile.Length)
        {
            throw new ArgumentException("Le chroma est plus court que le nombre de trames annonce.", nameof(chroma));
        }

        double[] mean = new double[MajorProfile.Length];
        if (frameCount == 0)
        {
            return mean;
        }

        for (int frame = 0; frame < frameCount; frame++)
        {
            int offset = frame * MajorProfile.Length;
            for (int pitchClass = 0; pitchClass < MajorProfile.Length; pitchClass++)
            {
                mean[pitchClass] += chroma[offset + pitchClass];
            }
        }

        for (int pitchClass = 0; pitchClass < MajorProfile.Length; pitchClass++)
        {
            mean[pitchClass] /= frameCount;
        }

        return mean;
    }

    private static void Add(
        List<KeyCandidate> candidates,
        int tonic,
        KeyMode mode,
        double[] profile,
        ReadOnlySpan<double> chroma)
    {
        double correlation = Pearson(chroma, Rotate(profile, tonic));
        string name = KeySignature.Of(PitchName(tonic), mode).ToString();
        if (!KeySignature.TryParse(name, out KeySignature? key) || key is null)
        {
            throw new InvalidOperationException($"La tonique « {name} » construite pour la classe {tonic} est invalide.");
        }

        candidates.Add(new KeyCandidate(key, correlation, 0));
    }

    /// <summary>Tourne un gabarit pour que sa tonique soit la classe demandee.</summary>
    /// <param name="profile">Le gabarit, indexe depuis la tonique.</param>
    /// <param name="tonic">La classe de hauteur de la tonique visee, 0 = do.</param>
    /// <returns>Le gabarit reindexe depuis la classe de la tonique.</returns>
    public static double[] Rotate(ReadOnlySpan<double> profile, int tonic)
    {
        if (profile.Length != MajorProfile.Length)
        {
            throw new ArgumentException($"Le gabarit doit compter {MajorProfile.Length} valeurs.", nameof(profile));
        }

        int shift = ((tonic % MajorProfile.Length) + MajorProfile.Length) % MajorProfile.Length;
        double[] rotated = new double[MajorProfile.Length];
        for (int index = 0; index < MajorProfile.Length; index++)
        {
            rotated[index] = profile[(index - shift + MajorProfile.Length) % MajorProfile.Length];
        }

        return rotated;
    }

    /// <summary>Correlation de Pearson entre deux vecteurs de meme longueur.</summary>
    /// <param name="left">Premier vecteur.</param>
    /// <param name="right">Second vecteur.</param>
    /// <returns>La correlation, ou 0 si l'un des vecteurs est constant.</returns>
    public static double Pearson(ReadOnlySpan<double> left, ReadOnlySpan<double> right)
    {
        if (left.Length != right.Length)
        {
            throw new ArgumentException("Les deux vecteurs doivent avoir la meme longueur.", nameof(right));
        }

        if (left.Length == 0)
        {
            return 0;
        }

        double meanLeft = 0;
        double meanRight = 0;
        for (int index = 0; index < left.Length; index++)
        {
            meanLeft += left[index];
            meanRight += right[index];
        }

        meanLeft /= left.Length;
        meanRight /= right.Length;

        double covariance = 0;
        double varianceLeft = 0;
        double varianceRight = 0;
        for (int index = 0; index < left.Length; index++)
        {
            double a = left[index] - meanLeft;
            double b = right[index] - meanRight;
            covariance += a * b;
            varianceLeft += a * a;
            varianceRight += b * b;
        }

        double denominator = Math.Sqrt(varianceLeft * varianceRight);
        return denominator > 0 ? covariance / denominator : 0;
    }

    private static string PitchName(int pitchClass)
    {
        string[] names = ["C", "Db", "D", "Eb", "E", "F", "Gb", "G", "Ab", "A", "Bb", "B"];
        return names[pitchClass];
    }
}
