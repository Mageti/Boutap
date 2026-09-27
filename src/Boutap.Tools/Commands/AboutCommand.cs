// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Boutap.Tools.Commands;

/// <summary>Affiche l'identite, la licence et la liste des commandes.</summary>
public sealed class AboutCommand : ICommand
{
    /// <inheritdoc/>
    public string Name => "about";

    /// <inheritdoc/>
    public string Summary => "Affiche qui fait l'outil, sous quelle licence, et ce qu'il sait faire.";

    /// <inheritdoc/>
    public string Usage => "boutap about";

    /// <inheritdoc/>
    public int Run(CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Out.WriteLine($"{ToolInfo.Name} {ToolInfo.Version}");
        context.Out.WriteLine(ToolInfo.Description);
        context.Out.WriteLine($"Licence du code : {ToolInfo.License}");
        context.Out.WriteLine($"Depot : {ToolInfo.RepositoryUrl}");
        context.Out.WriteLine();
        context.Out.WriteLine("Commandes :");

        foreach (ICommand command in CommandRegistry.All)
        {
            context.Out.WriteLine($"  {command.Name,-14} {command.Summary}");
        }

        return ExitCodes.Success;
    }
}
