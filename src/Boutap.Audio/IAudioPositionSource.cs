// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Boutap.Audio;

/// <summary>
/// Source de la position de lecture, en echantillons depuis le debut du morceau.
/// </summary>
/// <remarks>
/// L'interface existe pour que <see cref="AudioClock"/> puisse etre teste sans
/// carte son. En production, la source est le lecteur audio natif ; dans les
/// tests, c'est un compteur que l'on avance a la main.
/// </remarks>
public interface IAudioPositionSource
{
    /// <summary>Fréquence d'échantillonnage du flux, en Hz.</summary>
    double SampleRate { get; }

    /// <summary>Position de lecture courante, en echantillons depuis le debut.</summary>
    /// <remarks>
    /// Valeur negative possible avant le premier echantillon : la source peut
    /// demarrer en anticipation pour absorber la latence de sortie.
    /// </remarks>
    long FramePosition { get; }

    /// <summary>Vrai si la source avance de son propre chef.</summary>
    bool IsRunning { get; }
}
