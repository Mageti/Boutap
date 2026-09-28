// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Boutap.Core.Pack;

/// <summary>
/// Le fichier est illisible : ce n'est pas une archive, ou ce n'est pas du
/// JSON. Se distingue d'une validation_failed, qui dit qu'un pack est
/// parfaitement lisible mais ne respecte pas le format.
/// </summary>
public sealed class PackFormatException : Exception
{
    /// <summary>Cree l'exception.</summary>
    public PackFormatException(string message)
        : base(message)
    {
    }

    /// <summary>Cree l'exception avec sa cause.</summary>
    public PackFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
