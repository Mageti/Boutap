// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Details de presentation partages par les commandes.

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Boutap.Core.Pack;

namespace Boutap.Gen.Cli;

/// <summary>Ce que toutes les commandes ont besoin de mettre en forme.</summary>
internal static class GenJson
{
    /// <summary>Une seule ligne, sans espace inutile : c'est une sortie machine.</summary>
    public static JsonSerializerOptions Compact { get; } = new() { WriteIndented = false };

    /// <summary>Le nom d'un niveau, tel que la ligne de commande l'ecrit.</summary>
    /// <param name="level">Le niveau.</param>
    public static string Level(ChartLevel level) => ChartLevels.FileNameOf(level);

    /// <summary>Le nom d'une balise gapless, tel que le manifeste l'ecrit.</summary>
    /// <param name="tag">La balise.</param>
    public static string Gapless(GaplessTag tag) => tag switch
    {
        GaplessTag.Xing => "xing",
        GaplessTag.ITunSmpb => "itun-smpb",
        GaplessTag.Vbri => "vbri",
        GaplessTag.None => "none",
        _ => "unknown",
    };

    /// <summary>Un nombre arrondi, en culture invariante, dans l'objet JSON.</summary>
    /// <param name="value">La valeur.</param>
    /// <param name="digits">Le nombre de decimales.</param>
    public static double Round(double value, int digits = 6)
        => Math.Round(value, digits, MidpointRounding.AwayFromZero);

    /// <summary>Ecrit un nombre avec un nombre fixe de decimales.</summary>
    /// <param name="value">La valeur.</param>
    /// <param name="digits">Le nombre de decimales.</param>
    public static string Fixed(double value, int digits)
        => value.ToString("F" + digits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
}
