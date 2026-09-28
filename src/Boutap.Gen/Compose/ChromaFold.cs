// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Pliage des douze classes de hauteur sur les neuf touches : wiki generateur.md §5.2.
//
// On a douze classes de hauteur et neuf boutons. On ne peut pas tout mapper,
// alors on supprime les classes structurellement instables : la tritone (6
// demi-tons), la deuxieme mineure (1 demi-ton) et la troisieme de la gamme
// chromatique (4 demi-tons). Il reste {0, 2, 3, 5, 7, 8, 9, 10, 11}, soit
// l'echelle majeure et l'echelle mineure naturelle reunies : les neuf sons
// d'une gamme diatonique, ce qui est exactement ce qu'une manette a neuf
// boutons sait jouer.
//
// La specification ecrit « triton (4 demi-tons) ». Une tritone vaut six
// demi-tons ; quatre demi-tons, c'est la tierce majeure, qui n'a rien
// d'instable. C'est l'ensemble de neuf classes retenu qui fait foi, et il
// retire bien 1, 4 et 6. La divergence est consignee dans le wiki.

namespace Boutap.Gen.Compose;

/// <summary>Le passage de douze classes de hauteur a neuf touches.</summary>
public static class ChromaFold
{
    /// <summary>Nombre de touches de la grille.</summary>
    public const int GridSize = 9;

    /// <summary>Colonnes de la grille, lues ligne par ligne.</summary>
    /// <remarks>
    /// Les profils livres dans profiles/ declarent T0 a T8 dans cet ordre :
    /// une rangee de trois, puis la suivante. Cette fonction est donc la seule
    /// qui fait autorite sur la geometrie, et tout ce qui parle de voisinage
    /// passe par elle.
    /// </remarks>
    public const int Columns = 3;

    private static readonly int[] Kept = [0, 2, 3, 5, 7, 8, 9, 10, 11];

    /// <summary>Les neuf classes conservees, croissantes.</summary>
    public static IReadOnlyList<int> KeptClasses => Kept;

    /// <summary>Ramene une classe de hauteur sur la classe conservee la plus proche.</summary>
    /// <param name="pitchClass">La classe de hauteur, de 0 = do a 11 = si.</param>
    /// <returns>La classe conservee, entre 0 et 11.</returns>
    /// <remarks>
    /// A egalite de distance, on plie vers le haut. Plier vers le bas ferait
    /// disparaitre une note dans une classe deja presente plus bas, ce qui
    /// effacerait le son au lieu de le corriger.
    /// </remarks>
    public static int Fold(int pitchClass)
    {
        int value = Mod(pitchClass, 12);
        if (Array.IndexOf(Kept, value) >= 0)
        {
            return value;
        }

        int best = value;
        int bestDistance = int.MaxValue;
        foreach (int candidate in Kept)
        {
            int distance = Math.Abs(candidate - value);
            if (distance < bestDistance || (distance == bestDistance && candidate > best))
            {
                best = candidate;
                bestDistance = distance;
            }
        }

        return best;
    }

    /// <summary>Le numero de touche d'une classe de hauteur, apres pliage.</summary>
    /// <param name="pitchClass">La classe de hauteur, de 0 = do a 11 = si.</param>
    /// <returns>L'index de touche, de 0 a 8.</returns>
    public static int GridIndex(int pitchClass) => Array.IndexOf(Kept, Fold(pitchClass));

    /// <summary>La classe de hauteur d'une touche de la grille.</summary>
    /// <param name="gridIndex">L'index de touche, de 0 a 8.</param>
    /// <returns>La classe de hauteur conservee correspondante.</returns>
    public static int PitchClassAt(int gridIndex)
    {
        if (gridIndex < 0 || gridIndex >= GridSize)
        {
            throw new ArgumentOutOfRangeException(nameof(gridIndex), gridIndex, $"L'index de touche doit etre entre 0 et {GridSize - 1}.");
        }

        return Kept[gridIndex];
    }

    /// <summary>Replie un chroma de douze classes sur neuf touches.</summary>
    /// <param name="chroma">Le chroma, sur douze classes.</param>
    /// <returns>Neuf valeurs, une par touche.</returns>
    public static double[] FoldChroma(ReadOnlySpan<double> chroma)
    {
        if (chroma.Length != 12)
        {
            throw new ArgumentException($"Le chroma doit compter 12 classes, il en compte {chroma.Length}.", nameof(chroma));
        }

        double[] folded = new double[GridSize];
        for (int pitchClass = 0; pitchClass < 12; pitchClass++)
        {
            folded[GridIndex(pitchClass)] += chroma[pitchClass];
        }

        return folded;
    }

    /// <summary>Distance en nombre de touches entre deux touches voisines de la grille.</summary>
    /// <param name="left">Premier index de touche.</param>
    /// <param name="right">Second index de touche.</param>
    /// <returns>La distance de Manhattan sur la grille 3 par 3.</returns>
    /// <remarks>
    /// La distance de Manhattan et non euclidienne : on separe les mains, on
    /// ne contourne pas les touches. Un pas lateral et un pas vertical
    /// comptent pareil.
    /// </remarks>
    public static int Distance(int left, int right)
    {
        int rowLeft = left / Columns;
        int columnLeft = left % Columns;
        int rowRight = right / Columns;
        int columnRight = right % Columns;
        return Math.Abs(rowLeft - rowRight) + Math.Abs(columnLeft - columnRight);
    }

    /// <summary>Deux touches sont-elles voisines sur la grille ?</summary>
    /// <param name="left">Premier index de touche.</param>
    /// <param name="right">Second index de touche.</param>
    /// <returns><see langword="true"/> si elles se touchent, en angle droit ou non.</returns>
    public static bool AreAdjacent(int left, int right) => left != right && Distance(left, right) == 1;

    private static int Mod(int value, int modulus) => ((value % modulus) + modulus) % modulus;
}
