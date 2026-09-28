// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Globalization;
using System.Text.RegularExpressions;

namespace Boutap.Core.Pack;

/// <summary>
/// Les regles de champ du format, une par une. Toutes les valeurs viennent de
/// <c>schema/manifest.schema.json</c> et <c>schema/chart.schema.json</c> ; les
/// recopier ici permet de signaler un champ fautif avec un pointeur et un
/// message, ce que <c>additionalProperties: false</c> ne sait pas faire.
/// </summary>
public static partial class PackFieldRules
{
    /// <summary>Identifiant de pack : minuscules, tirets et soulignés.</summary>
    [GeneratedRegex(@"^[a-z0-9][a-z0-9_-]{2,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();

    /// <summary>Chemin relatif dans le pack.</summary>
    [GeneratedRegex(@"^[A-Za-z0-9._-]+(/[A-Za-z0-9._-]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex PathPattern();

    /// <summary>Emplacement d'une chart.</summary>
    [GeneratedRegex(@"^charts/[a-z0-9_-]+\.json$", RegexOptions.CultureInvariant)]
    private static partial Regex ChartFilePattern();

    /// <summary>Verifie qu'un identifiant de pack est bien forme.</summary>
    public static bool IsValidId(string? id) => id is not null && IdPattern().IsMatch(id);

    /// <summary>Verifie qu'un chemin relatif de pack est bien forme.</summary>
    public static bool IsValidPath(string? path) => path is not null && PathPattern().IsMatch(path);

    /// <summary>Verifie qu'un chemin de chart est bien forme.</summary>
    public static bool IsValidChartFile(string? file) => file is not null && ChartFilePattern().IsMatch(file);

    /// <summary>Verifie qu'un hexadecimal en minuscules de 64 caracteres est bien forme.</summary>
    public static bool IsValidSha256(string? hash) => Common.Sha256Hex.IsLowerHex64(hash);

    /// <summary>Verifie qu'un decalage de phase est l'un des quatre autorises.</summary>
    /// <remarks>
    /// Les valeurs sont des fractions de temps musical. Comparer des flottants
    /// avec <c>==</c> serait ici une erreur de methode : ces quatre valeurs-la
    /// sont exactement representables en binaire, et le test se fait sur la
    /// liste, pas sur une tolerance.
    /// </remarks>
    public static bool IsValidPhaseShift(double value) => value switch
    {
        0 or 0.125 or 0.25 or 0.375 => true,
        _ => false,
    };

    /// <summary>Les quatre decalages de phase, dans l'ordre du schema.</summary>
    public static IReadOnlyList<double> AllowedPhaseShifts { get; } =
        new[] { 0d, 0.125d, 0.25d, 0.375d };

    /// <summary>Verifie qu'un nombre est dans un intervalle ferme.</summary>
    public static bool InRange(double value, double min, double max) => value >= min && value <= max;

    /// <summary>Formate un nombre pour un message d'erreur, en culture invariante.</summary>
    public static string Num(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>
    /// Verifie qu'un champ obligatoire est present, et signale son absence avec
    /// le bon pointeur.
    /// </summary>
    /// <param name="result">Resultat en cours.</param>
    /// <param name="present">Le champ est-il present et non vide ?</param>
    /// <param name="code">Code a employer.</param>
    /// <param name="jsonPointer">Pointeur du champ.</param>
    /// <param name="what">Nom du champ, pour le message.</param>
    /// <returns><paramref name="present"/>.</returns>
    public static bool Required(
        ValidationResult result,
        bool present,
        string code,
        string jsonPointer,
        string what)
    {
        if (!present)
        {
            result.Error(code, $"{what} est obligatoire.", jsonPointer);
        }

        return present;
    }
}
