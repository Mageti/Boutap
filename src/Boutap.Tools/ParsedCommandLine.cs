// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Boutap.Tools;

/// <summary>Ce que <see cref="CommandLine"/> a compris d'une ligne de commande.</summary>
/// <param name="Command">Nom de la commande, ou <see langword="null"/> si aucune.</param>
/// <param name="Positionals">Arguments positionnels, dans l'ordre.</param>
/// <param name="Options">Options nommées et leur valeur eventuelle.</param>
/// <param name="ShowHelp">Vrai si l'appelant a demande l'aide.</param>
public sealed record ParsedCommandLine(
    string? Command,
    IReadOnlyList<string> Positionals,
    IReadOnlyDictionary<string, string?> Options,
    bool ShowHelp)
{
    /// <summary>Vrai si l'option est presente.</summary>
    /// <param name="name">Nom de l'option, sans les deux tirets.</param>
    /// <returns>Vrai si l'appelant a passe l'option.</returns>
    public bool Has(string name) => Options.ContainsKey(name);

    /// <summary>Lit la valeur d'une option.</summary>
    /// <param name="name">Nom de l'option, sans les deux tirets.</param>
    /// <param name="value">Valeur trouvee, si l'option en a une.</param>
    /// <returns>
    /// Vrai si l'option est presente **et** porte une valeur. Un drapeau
    /// donne faux : <c>--warn</c> existe, mais n'a pas de valeur, et une
    /// commande qui demande une valeur doit pouvoir le savoir.
    /// </returns>
    public bool TryGetValue(string name, out string? value)
    {
        if (Options.TryGetValue(name, out string? found) && found is not null)
        {
            value = found;
            return true;
        }

        value = null;
        return false;
    }

    /// <summary>Lit la valeur d'une option, ou une valeur de repli.</summary>
    /// <param name="name">Nom de l'option, sans les deux tirets.</param>
    /// <param name="fallback">Valeur rendue si l'option est absente.</param>
    /// <returns>La valeur de l'option, ou <paramref name="fallback"/>.</returns>
    public string? ValueOr(string name, string? fallback) =>
        TryGetValue(name, out string? value) && value is not null ? value : fallback;
}
