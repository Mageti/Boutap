// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Boutap.Audio;

/// <summary>Ce que l'en-tete d'un fichier WAV declare.</summary>
/// <param name="SampleRate">Frequence d'echantillonnage, en Hz.</param>
/// <param name="Channels">Nombre de canaux, de 1 a 8.</param>
/// <param name="BitsPerSample">Bits par echantillon stockes : 8, 16, 24, 32 ou 64.</param>
/// <param name="IsFloat">Vrai si les echantillons sont en virgule flottante IEEE 754.</param>
/// <param name="FrameCount">Nombre d'images stereo completes.</param>
/// <param name="DataOffset">Position du chunk <c>data</c> dans le fichier.</param>
/// <param name="DataLength">Taille du chunk <c>data</c>, en octets.</param>
public sealed record WavInfo(
    int SampleRate,
    int Channels,
    int BitsPerSample,
    bool IsFloat,
    long FrameCount,
    long DataOffset,
    long DataLength)
{
    /// <summary>Nombre total d'echantillons, tous canaux confondus.</summary>
    public long SampleCount => FrameCount * Channels;

    /// <summary>Duree du flux, en secondes.</summary>
    public double DurationSeconds => SampleRate > 0 ? (double)FrameCount / SampleRate : 0.0;
}
