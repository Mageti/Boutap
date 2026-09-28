// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Generation par accords : wiki generateur.md §5.2.
//
// L'index 2 est un bouton, pas un timbre. Si l'on jouait les onsets, il
// faudrait choisir arbitrairement neuf hauteurs parmi douze, et ce choix serait
// un arbitrage. Si l'on joue les accords, chaque note appartient a un accord,
// et l'accord est determine par l'harmonie du morceau. C'est la difference
// entre une partition et un tirage au sort.

using System.Collections.ObjectModel;

using Boutap.Core.Common;

namespace Boutap.Gen.Compose;

/// <summary>Un accord candidat, et les touches de grille qu'il occupe.</summary>
/// <param name="Root">La classe de hauteur de la tonique, de 0 = do a 11 = si.</param>
/// <param name="Mode">Le mode de l'accord.</param>
/// <param name="GridNotes">Les index de touche occupes, sans repetition.</param>
public sealed record Chord(int Root, KeyMode Mode, IReadOnlyList<int> GridNotes)
{
    /// <summary>Le nom de l'accord, tel qu'il sera inscrit dans la partition.</summary>
    public KeySignature Key => KeySignature.Of(TonicName, Mode);

    /// <summary>Le nom de la tonique, en graphie plate.</summary>
    public string TonicName => RootNames[Root];

    /// <summary>Les intervalles, en demi-tons depuis la tonique, avant pliage.</summary>
    public IReadOnlyList<int> Intervals => Mode == KeyMode.Minor ? [0, 3, 7] : [0, 4, 7];

    private static readonly string[] RootNames = ["C", "Db", "D", "Eb", "E", "F", "Gb", "G", "Ab", "A", "Bb", "B"];
}

/// <summary>Les vingt-quatre accords, et le choix de l'accord dominant.</summary>
public static class ChordMapper
{
    private static readonly Chord[] All = BuildAll();

    /// <summary>Les vingt-quatre accords, tous modes confondus.</summary>
    public static IReadOnlyList<Chord> Candidates => new ReadOnlyCollection<Chord>(All);

    /// <summary>L'accord d'une classe de hauteur et d'un mode.</summary>
    /// <param name="root">La classe de hauteur de la tonique.</param>
    /// <param name="mode">Le mode de l'accord.</param>
    /// <returns>L'accord correspondant.</returns>
    public static Chord Of(int root, KeyMode mode)
    {
        int index = (((root % 12) + 12) % 12) * 2 + (mode == KeyMode.Minor ? 1 : 0);
        return All[index];
    }

    /// <summary>Choisit l'accord qui explique le mieux un chroma moyen.</summary>
    /// <param name="meanChroma">Le chroma moyen de la fenetre, sur douze classes.</param>
    /// <returns>L'accord le plus proche, jamais <see langword="null"/>.</returns>
    /// <remarks>
    /// Le score est l'energie des trois notes de l'accord dans le chroma de la
    /// fenetre. C'est un choix grossier et volontaire : sur une fenetre d'un
    /// temps, la theorie harmonique fine n'a pas plus de signal que cela, et
    /// une mesure savante serait une illusion de precision.
    /// </remarks>
    public static Chord Best(ReadOnlySpan<double> meanChroma)
    {
        if (meanChroma.Length != 12)
        {
            throw new ArgumentException($"Le chroma doit compter 12 classes, il en compte {meanChroma.Length}.", nameof(meanChroma));
        }

        Chord best = All[0];
        double bestScore = double.NegativeInfinity;
        foreach (Chord chord in All)
        {
            double score = 0;
            foreach (int interval in chord.Intervals)
            {
                score += meanChroma[(chord.Root + interval) % 12];
            }

            if (score > bestScore)
            {
                bestScore = score;
                best = chord;
            }
        }

        return best;
    }

    /// <summary>Le score d'un accord sur un chroma moyen.</summary>
    /// <param name="meanChroma">Le chroma moyen de la fenetre, sur douze classes.</param>
    /// <param name="chord">L'accord a evaluer.</param>
    /// <returns>L'energie cumulee de ses trois notes.</returns>
    public static double Score(ReadOnlySpan<double> meanChroma, Chord chord)
    {
        double score = 0;
        foreach (int interval in chord.Intervals)
        {
            score += meanChroma[(chord.Root + interval) % 12];
        }

        return score;
    }

    private static Chord[] BuildAll()
    {
        List<Chord> chords = new(24);
        for (int root = 0; root < 12; root++)
        {
            foreach (KeyMode mode in (KeyMode[])[KeyMode.Major, KeyMode.Minor])
            {
                HashSet<int> notes = [];
                foreach (int interval in mode == KeyMode.Minor ? new[] { 0, 3, 7 } : new[] { 0, 4, 7 })
                {
                    notes.Add(ChromaFold.GridIndex(root + interval));
                }

                chords.Add(new Chord(root, mode, new ReadOnlyCollection<int>(notes.Order().ToArray())));
            }
        }

        return chords.ToArray();
    }
}
