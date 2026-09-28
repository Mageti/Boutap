// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Boutap.Tools;

/// <summary>Ce dont une commande a besoin pour s'executer.</summary>
/// <remarks>
/// Les flux de sortie sont injectes plutot que lus dans <see cref="Console"/>
/// directement : c'est ce qui rend les commandes testables sans capture de
/// console, et ce qui evite qu'un test ecrase la sortie du testeur.
/// </remarks>
public sealed class CommandContext
{
    private readonly ParsedCommandLine _parsed;

    /// <summary>Construit le contexte d'une commande.</summary>
    /// <param name="parsed">Resultat de l'analyse de la ligne de commande.</param>
    /// <param name="out">Flux de sortie ordinaire.</param>
    /// <param name="error">Flux de diagnostic.</param>
    public CommandContext(ParsedCommandLine parsed, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(parsed);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        _parsed = parsed;
        Out = output;
        Error = error;
    }

    /// <summary>Flux de sortie ordinaire.</summary>
    public TextWriter Out { get; }

    /// <summary>Flux de diagnostic et d'erreurs.</summary>
    public TextWriter Error { get; }

    /// <summary>Arguments positionnels, commande comprise en tete.</summary>
    public IReadOnlyList<string> Positionals => _parsed.Positionals;

    /// <summary>Options nommees.</summary>
    public IReadOnlyDictionary<string, string?> Options => _parsed.Options;

    /// <summary>Vrai si l'option est presente.</summary>
    /// <param name="name">Nom de l'option, sans les deux tirets.</param>
    /// <returns>Vrai si l'appelant a passe l'option.</returns>
    public bool Has(string name) => _parsed.Has(name);

    /// <summary>Lit la valeur d'une option.</summary>
    /// <param name="name">Nom de l'option, sans les deux tirets.</param>
    /// <param name="value">Valeur trouvee, si l'option en a une.</param>
    /// <returns>Vrai si l'option est presente et porte une valeur.</returns>
    public bool TryGetValue(string name, out string? value) => _parsed.TryGetValue(name, out value);

    /// <summary>Lit la valeur d'une option, ou une valeur de repli.</summary>
    /// <param name="name">Nom de l'option, sans les deux tirets.</param>
    /// <param name="fallback">Valeur rendue si l'option est absente.</param>
    /// <returns>La valeur de l'option, ou <paramref name="fallback"/>.</returns>
    public string? ValueOr(string name, string? fallback) => _parsed.ValueOr(name, fallback);

    /// <summary>
    /// Les arguments d'une commande, commande et sous-commande retirees.
    /// </summary>
    /// <param name="skip">Nombre d'elements a retirer du debut.</param>
    /// <returns>Les arguments restants.</returns>
    public IReadOnlyList<string> Rest(int skip = 1) =>
        _parsed.Positionals.Count <= skip
            ? Array.Empty<string>()
            : _parsed.Positionals.Skip(skip).ToArray();

    /// <summary>Ecrit une ligne de diagnostic.</summary>
    /// <param name="message">Texte a ecrire.</param>
    public void Warn(string message) => Error.WriteLine(message);

    /// <summary>
    /// Ecrit une phrase d'explication sur la sortie d'erreur.
    /// </summary>
    /// <remarks>
    /// Distinct de <see cref="Warn"/> par l'intention, pas par la sortie :
    /// une bannière, un rappel de ce qui a été désactivé ou un résumé de
    /// traitement ne sont pas des avertissements. C'est ce qui permet à
    /// <c>--json</c> de laisser la sortie standard entièrement parseable :
    /// tout ce qui n'est pas le résultat est sur l'erreur.
    /// </remarks>
    /// <param name="message">Texte à écrire, sans préfixe.</param>
    public void Note(string message) => Error.WriteLine(message);
}
