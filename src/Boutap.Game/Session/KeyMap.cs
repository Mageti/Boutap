// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Boutap.Core.Pack;

namespace Boutap.Game.Session;

/// <summary>La geometrie du pave 3x3, et son voisinage.</summary>
/// <remarks>
/// <para>
/// <see cref="KeyBinding"/> expose volontairement une touche comme une valeur
/// opaque : il sait dire si c'est une touche de grille ou un volant, pas
/// laquelle. C'est assez pour ecrire une chart et la valider. Ce n'est pas
/// assez pour jouer : arbitrer un coup demande de savoir si deux touches se
/// touchent sur le pave.
/// </para>
/// <para>
/// Ce type est le seul endroit du projet ou cette geometrie existe. Le pave est
/// trois colonnes et trois rangees (wiki: spec.md §4.1) ; deux touches sont
/// voisines quand elles partagent un cote, pas un coin : un coup porte sur le
/// coin en diagonale est une erreur de doigt de deux cases, et la reconnaitre
/// comme visee rendrait l'aide au geste trop Large pour discriminer quoi que ce
/// soit.
/// </para>
/// </remarks>
public static class KeyMap
{
    /// <summary>Colonnes du pave.</summary>
    public const int Columns = 3;

    /// <summary>Rangees du pave.</summary>
    public const int Rows = KeyBinding.GridSize / Columns;

    /// <summary>Nombre de cases de la table de correspondance touches a créneaux.</summary>
    /// <remarks>
    /// Les neuf touches de la grille, puis les deux volants. Une table plate
    /// evite un dictionnaire par coup porte.
    /// </remarks>
    public const int SlotCount = KeyBinding.GridSize + 2;

    private const int WheelLeftSlot = KeyBinding.GridSize;
    private const int WheelRightSlot = KeyBinding.GridSize + 1;

    private static readonly KeyBinding[] Grid = BuildGrid();

    private static KeyBinding[] BuildGrid()
    {
        var grid = new KeyBinding[KeyBinding.GridSize];
        for (int index = 0; index < grid.Length; index++)
        {
            grid[index] = KeyBinding.Grid(index);
        }

        return grid;
    }

    /// <summary>La touche de la grille a cet index.</summary>
    /// <param name="index">Index, de 0 a 8, lu ligne par ligne.</param>
    /// <returns>La touche.</returns>
    public static KeyBinding At(int index) => Grid[index];

    /// <summary>Index de la grille pour une touche, ou -1 si ce n'en est pas une.</summary>
    /// <param name="key">La touche.</param>
    /// <returns>L'index, ou -1.</returns>
    public static int IndexOf(KeyBinding key) => Array.IndexOf(Grid, key);

    /// <summary>Creneau d'une touche dans une table plate.</summary>
    /// <param name="key">La touche.</param>
    /// <returns>L'index de creneau, ou -1 si la touche n'est pas initialisee.</returns>
    public static int SlotOf(KeyBinding key)
    {
        int index = IndexOf(key);
        if (index >= 0)
        {
            return index;
        }

        return key == KeyBinding.WheelLeft
            ? WheelLeftSlot
            : key == KeyBinding.WheelRight ? WheelRightSlot : -1;
    }

    /// <summary>Vrai si deux touches se touchent sur le pave, cote contre cote.</summary>
    /// <param name="left">Premiere touche.</param>
    /// <param name="right">Seconde touche.</param>
    /// <returns>Vrai si elles sont voisines.</returns>
    public static bool AreAdjacent(KeyBinding left, KeyBinding right)
    {
        int a = IndexOf(left);
        int b = IndexOf(right);
        if (a < 0 || b < 0 || a == b)
        {
            return false;
        }

        int rowA = a / Columns;
        int columnA = a % Columns;
        int rowB = b / Columns;
        int columnB = b % Columns;

        int rowDelta = Math.Abs(rowA - rowB);
        int columnDelta = Math.Abs(columnA - columnB);
        return rowDelta + columnDelta == 1;
    }
}
