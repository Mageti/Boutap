// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Boutap.Core.Determinism;

/// <summary>
/// Derivation de la graine du generateur, telle que fixee par l'ADR 0005 :
/// <c>graine = SHA-256( sha256(audio) || version_generateur || niveau )</c>.
/// </summary>
/// <remarks>
/// Les 16 premiers octets du condensat forment l'etat 128 bits de
/// <see cref="Xorshift128Plus"/>. Aucun octet de nom de fichier n'entre dans
/// la graine : renommer un fichier ne doit pas changer le chart produit.
/// </remarks>
public static class SeedDerivation
{
    /// <summary>Calcule la graine d'un chart a partir de son audio et de son contexte.</summary>
    /// <param name="audioSha256Hex">
    /// SHA-256 de l'audio decode, en hexadecimal minuscule (64 caracteres).
    /// </param>
    /// <param name="generatorVersion">Version du generateur, telle qu'inscrite au manifeste.</param>
    /// <param name="levelName">Nom du niveau, par exemple <c>berceau</c>.</param>
    /// <returns>Les 16 octets de graine, en boutisme.</returns>
    /// <exception cref="ArgumentException">Un parametre est vide ou mal forme.</exception>
    public static byte[] Derive(string audioSha256Hex, string generatorVersion, string levelName)
    {
        if (string.IsNullOrEmpty(audioSha256Hex))
        {
            throw new ArgumentException("Le SHA-256 de l'audio est obligatoire.", nameof(audioSha256Hex));
        }

        ArgumentNullException.ThrowIfNull(generatorVersion);

        if (string.IsNullOrEmpty(levelName))
        {
            throw new ArgumentException("Le nom du niveau est obligatoire.", nameof(levelName));
        }

        // Le separateur '\0' rend la concatenation injective : sans lui,
        // ("1.0", "abc") et ("1.0a", "bc") donneraient la meme graine.
        byte[] payload = Encoding.UTF8.GetBytes(generatorVersion + "\0" + levelName);
        byte[] audioBytes = new byte[audioSha256Hex.Length / 2];
        ParseHex(audioSha256Hex, audioBytes);

        byte[] material = new byte[audioBytes.Length + payload.Length];
        audioBytes.CopyTo(material, 0);
        payload.CopyTo(material, audioBytes.Length);

        byte[] digest = SHA256.HashData(material);
        byte[] seed = new byte[Xorshift128Plus.SeedByteLength];
        digest.AsSpan(0, seed.Length).CopyTo(seed);
        return seed;
    }

    /// <summary>Derive directement le generateur associe a un chart.</summary>
    /// <param name="audioSha256Hex">SHA-256 de l'audio decode, en hexadecimal minuscule.</param>
    /// <param name="generatorVersion">Version du generateur.</param>
    /// <param name="levelName">Nom du niveau.</param>
    /// <returns>Un generateur pret a l'emploi.</returns>
    public static Xorshift128Plus CreateGenerator(string audioSha256Hex, string generatorVersion, string levelName)
    {
        byte[] seed = Derive(audioSha256Hex, generatorVersion, levelName);
        ulong s0 = BinaryPrimitives.ReadUInt64BigEndian(seed.AsSpan(0, 8));
        ulong s1 = BinaryPrimitives.ReadUInt64BigEndian(seed.AsSpan(8, 8));
        return Xorshift128Plus.Create(s0, s1);
    }

    private static void ParseHex(string text, Span<byte> destination)
    {
        for (int i = 0; i < destination.Length; i++)
        {
            int high = HexValue(text[i * 2]);
            int low = HexValue(text[(i * 2) + 1]);
            destination[i] = (byte)((high << 4) | low);
        }
    }

    private static int HexValue(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        _ => throw new FormatException($"Caractere hexadecimal invalide : '{c}'."),
    };
}
