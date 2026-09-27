// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Boutap.Tools;

/// <summary>La ligne de commande est mal formee.</summary>
public sealed class CommandLineException : Exception
{
    /// <summary>Construit l'exception avec un message.</summary>
    /// <param name="message">Ce qui ne va pas dans les arguments.</param>
    public CommandLineException(string message)
        : base(message)
    {
    }
}
