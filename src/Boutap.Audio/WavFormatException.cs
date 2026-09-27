// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Boutap.Audio;

/// <summary>Le fichier n'est pas un WAV que l'on sait lire.</summary>
public sealed class WavFormatException : Exception
{
    /// <summary>Construit l'exception avec un message.</summary>
    /// <param name="message">Ce qui ne va pas dans le fichier.</param>
    public WavFormatException(string message)
        : base(message)
    {
    }

    /// <summary>Construit l'exception avec un message et la cause d'origine.</summary>
    /// <param name="message">Ce qui ne va pas dans le fichier.</param>
    /// <param name="innerException">Erreur de lecture sous-jacente.</param>
    public WavFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
