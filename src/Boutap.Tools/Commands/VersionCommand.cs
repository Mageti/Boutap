// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Boutap.Tools.Commands;

/// <summary>Affiche le nom et la version de l'outil.</summary>
public sealed class VersionCommand : ICommand
{
    /// <inheritdoc/>
    public string Name => "version";

    /// <inheritdoc/>
    public string Summary => "Affiche la version de l'outil.";

    /// <inheritdoc/>
    public string Usage => "boutap version";

    /// <inheritdoc/>
    public IReadOnlyList<string> Flags => [];

    public int Run(CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Out.WriteLine($"{ToolInfo.Name} {ToolInfo.Version}");
        return ExitCodes.Success;
    }
}
