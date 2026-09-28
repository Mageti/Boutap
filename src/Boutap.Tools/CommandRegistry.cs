// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Boutap.Tools.Commands;

namespace Boutap.Tools;

/// <summary>Les commandes de l'outil, et la facon de les trouver par leur nom.</summary>
public static class CommandRegistry
{
    private static readonly ICommand[] CommandsArray =
    [
        new VersionCommand(),
        new AboutCommand(),
        new ValidateCommand(),
        new ShowCommand(),
        new AuditCommand(),
        new BenchCiCommand(),
        new BenchLatencyCommand(),
        new BenchInputCommand(),
    ];

    private static readonly Dictionary<string, ICommand> ByName = BuildIndex();

    /// <summary>Toutes les commandes, dans l'ordre d'affichage.</summary>
    public static IReadOnlyList<ICommand> All => CommandsArray;

    /// <summary>Donne les drapeaux d'une commande, pour le lecteur de ligne de commande.</summary>
    /// <param name="name">Nom de la commande.</param>
    /// <returns>Les options sans valeur, ou une liste vide si la commande est inconnue.</returns>
    public static IReadOnlyList<string> FlagsOf(string name) =>
        Find(name) is ICommand command ? command.Flags : [];

    /// <summary>Trouve une commande par son nom.</summary>
    /// <param name="name">Nom de la commande.</param>
    /// <returns>La commande, ou <see langword="null"/> si le nom est inconnu.</returns>
    public static ICommand? Find(string? name)
    {
        if (name is null)
        {
            return null;
        }

        return ByName.TryGetValue(name, out ICommand? command) ? command : null;
    }

    private static Dictionary<string, ICommand> BuildIndex()
    {
        Dictionary<string, ICommand> index = new(StringComparer.Ordinal);
        foreach (ICommand command in CommandsArray)
        {
            index[command.Name] = command;
        }

        return index;
    }
}
