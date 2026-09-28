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
/// <para>
/// Deux graines existent, et la difference n'est pas un detail :
/// <list type="bullet">
/// <item><description>
/// <b>La graine de pack</b> depend de l'audio et de la version du generateur
/// seulement. C'est celle que porte <c>generator.seed</c> du manifeste, parce
/// qu'un pack n'a pas de niveau.
/// </description></item>
/// <item><description>
/// <b>La graine de chart</b> ajoute le nom du niveau. Deux niveaux d'un meme
/// morceau doivent donc donner deux suites pseudo-aleatoires differentes, sans
/// quoi ils se ressembleraient.
/// </description></item>
/// </list>
/// </para>
/// <para>
/// Aucun octet de nom de fichier n'entre dans la graine : renommer un fichier
/// ne doit pas changer le chart produit. C'est ce qui rend la graine
/// reproductible — on peut la recalculer depuis le seul manifeste, et le
/// validateur s'en sert pour verifier qu'un pack n'a pas ete retouche.
/// </para>
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
        if (string.IsNullOrEmpty(levelName))
        {
            throw new ArgumentException("Le nom du niveau est obligatoire.", nameof(levelName));
        }

        return DeriveCore(audioSha256Hex, generatorVersion, levelName);
    }

    /// <summary>Calcule la graine d'un pack : sans niveau.</summary>
    /// <param name="audioSha256Hex">SHA-256 de l'audio decode, en hexadecimal minuscule.</param>
    /// <param name="generatorVersion">Version du generateur.</param>
    /// <returns>Les 16 octets de graine, en boutisme.</returns>
    public static byte[] DerivePack(string audioSha256Hex, string generatorVersion) =>
        DeriveCore(audioSha256Hex, generatorVersion, levelName: null);

    /// <summary>
    /// La valeur de <c>generator.seed</c> d'un pack : les 8 premiers octets de
    /// la graine de pack, lus en boutisme.
    /// </summary>
    /// <param name="audioSha256Hex">SHA-256 de l'audio decode.</param>
    /// <param name="generatorVersion">Version du generateur.</param>
    /// <returns>La graine telle qu'ecrite dans le manifeste.</returns>
    public static ulong PackSeedValue(string audioSha256Hex, string generatorVersion) =>
        SeedValue(DerivePack(audioSha256Hex, generatorVersion));

    /// <summary>
    /// La valeur de <c>generator.seed</c> d'une chart : les 8 premiers octets de
    /// la graine de chart, lus en boutisme.
    /// </summary>
    /// <param name="audioSha256Hex">SHA-256 de l'audio decode.</param>
    /// <param name="generatorVersion">Version du generateur.</param>
    /// <param name="levelName">Nom du niveau.</param>
    /// <returns>La graine telle qu'ecrite dans la chart.</returns>
    public static ulong ChartSeedValue(string audioSha256Hex, string generatorVersion, string levelName) =>
        SeedValue(Derive(audioSha256Hex, generatorVersion, levelName));

    /// <summary>Les 8 premiers octets d'une graine de 16 octets, lus en boutisme.</summary>
    /// <param name="seed">Graine complete.</param>
    /// <returns>La valeur 64 bits ecrite dans le format.</returns>
    public static ulong SeedValue(ReadOnlySpan<byte> seed)
    {
        if (seed.Length < 8)
        {
            throw new ArgumentException("Une graine fait au moins 8 octets.", nameof(seed));
        }

        return BinaryPrimitives.ReadUInt64BigEndian(seed);
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

    private static byte[] DeriveCore(string audioSha256Hex, string generatorVersion, string? levelName)
    {
        if (string.IsNullOrEmpty(audioSha256Hex))
        {
            throw new ArgumentException("Le SHA-256 de l'audio est obligatoire.", nameof(audioSha256Hex));
        }

        ArgumentNullException.ThrowIfNull(generatorVersion);

        if (audioSha256Hex.Length % 2 != 0)
        {
            throw new ArgumentException(
                $"Un SHA-256 hexadecimal fait 64 caracteres, pas {audioSha256Hex.Length}.",
                nameof(audioSha256Hex));
        }

        // Le separateur '\0' rend la concatenation injective : sans lui,
        // ("1.0", "abc") et ("1.0a", "bc") donneraient la meme graine.
        byte[] payload = Encoding.UTF8.GetBytes(
            levelName is null ? generatorVersion + "\0" : generatorVersion + "\0" + levelName);
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
