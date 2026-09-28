// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Boutap.Tools;

/// <summary>Une commande de l'outil.</summary>
public interface ICommand
{
    /// <summary>Nom tapes sur la ligne de commande, sans tirets.</summary>
    string Name { get; }

    /// <summary>Une ligne decrivant ce que fait la commande.</summary>
    string Summary { get; }

    /// <summary>La ligne d'usage affichee par <c>boutap help &lt;commande&gt;</c>.</summary>
    string Usage { get; }

    /// <summary>
    /// Les options de cette commande qui ne prennent jamais de valeur.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>validate --json pack.btp</c> doit se lire « en JSON, sur ce pack ».
    /// Sans cette declaration, un lecteur qui ignore tout de la commande ne
    /// peut pas distinguer ce drapeau de <c>--level ronde</c> : il prendrait
    /// <c>pack.btp</c> comme sa valeur et ne verrait plus de fichier a valider.
    /// </para>
    /// <para>
    /// Les drapeaux globaux (<c>--help</c>, <c>--version</c>, <c>--verbose</c>)
    /// n'ont pas besoin d'etre declares : avant la commande, toute option sans
    /// signe egal est un drapeau par construction.
    /// </para>
    /// </remarks>
    IReadOnlyList<string> Flags { get; }

    /// <summary>Execute la commande.</summary>
    /// <param name="context">Arguments, options et flux de sortie.</param>
    /// <returns>Un code parmi ceux de <see cref="ExitCodes"/>.</returns>
    int Run(CommandContext context);
}
