// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Chargement d'un WAV pour la generation.

using System.Runtime.InteropServices;

using Boutap.Audio;
using Boutap.Core.Common;
using Boutap.Core.Pack;

namespace Boutap.Gen.Cli;

/// <summary>Un audio decode, pret a etre analyse.</summary>
/// <param name="Samples">Signal, forme canonique.</param>
/// <param name="SampleRate">Frequence d'echantillonnage.</param>
/// <param name="DurationSeconds">Duree annoncee par l'en-tete.</param>
/// <param name="CanonicalSha256Hex">Empreinte du signal decode.</param>
/// <param name="CanonicalBytes">Le WAV d'origine, tel qu'il sera stocke.</param>
/// <param name="Gapless">Balise gapless, si l'en-tete en porte une.</param>
internal sealed record LoadedAudio(
    double[] Samples,
    int SampleRate,
    double DurationSeconds,
    string CanonicalSha256Hex,
    byte[] CanonicalBytes,
    GaplessTag Gapless);

/// <summary>Lit un WAV depuis le disque.</summary>
internal static class AudioLoader
{
    /// <summary>Charge un fichier WAV.</summary>
    /// <param name="path">Chemin du fichier.</param>
    public static LoadedAudio Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Fichier audio introuvable.", path);
        }

        byte[] bytes = File.ReadAllBytes(path);
        using var stream = new MemoryStream(bytes, writable: false);
        WavInfo info = WavDecoder.ReadInfo(stream);
        stream.Position = 0;
        float[] canonical = WavDecoder.DecodeToCanonical(stream, info);

        // L'empreinte porte sur la forme canonique, en flottants 32 bits, et
        // jamais sur le fichier : deux WAV qui sonnent pareil doivent donner la
        // meme graine. Elargir a double avant de hacher changerait l'empreinte,
        // donc on hache d'abord, on analyse ensuite.
        string digest = Sha256Hex.OfBytesHex(MemoryMarshal.AsBytes<float>(canonical));

        var samples = new double[canonical.Length];
        for (int i = 0; i < canonical.Length; i++)
        {
            samples[i] = canonical[i];
        }

        return new LoadedAudio(
            samples,
            info.SampleRate,
            info.DurationSeconds,
            digest,
            bytes,
            WavGapless.Read(bytes));
    }
}
