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

    /// <summary>Execute la commande.</summary>
    /// <param name="context">Arguments, options et flux de sortie.</param>
    /// <returns>Un code parmi ceux de <see cref="ExitCodes"/>.</returns>
    int Run(CommandContext context);
}
