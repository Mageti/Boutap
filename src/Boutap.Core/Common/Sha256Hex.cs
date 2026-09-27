// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Security.Cryptography;

namespace Boutap.Core.Common;

/// <summary>
/// Utilitaires SHA-256. Le format `.btp` stocke les condensats en
/// hexadecimal minuscule sur 64 caracteres ; ces methodes sont le seul endroit
/// du projet ou cette forme est produite ou consommee.
/// </summary>
public static class Sha256Hex
{
    /// <summary>Longueur d'un condensat SHA-256, en octets.</summary>
    public const int DigestLength = 32;

    /// <summary>Longueur d'un condensat SHA-256, en caracteres hexadecimales.</summary>
    public const int HexLength = 64;

    /// <summary>Calcule le SHA-256 d'un tableau d'octets.</summary>
    /// <param name="data">Donnees a hacher.</param>
    /// <returns>Le condensat brut, 32 octets.</returns>
    public static byte[] Hash(ReadOnlySpan<byte> data) => SHA256.HashData(data);

    /// <summary>Calcule le SHA-256 d'un fichier, lu par blocs.</summary>
    /// <param name="path">Chemin du fichier.</param>
    /// <returns>Le condensat brut, 32 octets.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> est vide.</exception>
    /// <exception cref="System.IO.FileNotFoundException">Le fichier n'existe pas.</exception>
    public static byte[] HashFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        using FileStream stream = File.OpenRead(path);
        return SHA256.HashData(stream);
    }

    /// <summary>Calcule le SHA-256 d'un fichier et le rend en hexadecimal minuscule.</summary>
    /// <param name="path">Chemin du fichier.</param>
    /// <returns>64 caracteres hexadecimales minuscules.</returns>
    public static string HashFileHex(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return ToHex(HashFile(path));
    }

    /// <summary>Convertit un condensat en hexadecimal minuscule.</summary>
    /// <param name="digest">Condensat.</param>
    /// <returns>64 caracteres hexadecimales minuscules.</returns>
    /// <exception cref="ArgumentException"><paramref name="digest"/> ne fait pas 32 octets.</exception>
    public static string ToHex(ReadOnlySpan<byte> digest)
    {
        if (digest.Length != DigestLength)
        {
            throw new ArgumentException(
                $"Un condensat SHA-256 fait {DigestLength} octets, celui-ci en fait {digest.Length}.",
                nameof(digest));
        }

        return Convert.ToHexString(digest).ToLowerInvariant();
    }

    /// <summary>Calcule le SHA-256 de donnees et le rend en hexadecimal minuscule.</summary>
    /// <param name="data">Donnees a hacher.</param>
    /// <returns>64 caracteres hexadecimales minuscules.</returns>
    public static string OfBytesHex(ReadOnlySpan<byte> data) => ToHex(Hash(data));

    /// <summary>Verifie qu'un texte est bien un condensat hexadecimal minuscule.</summary>
    /// <param name="text">Texte a verifier, eventuellement nul.</param>
    /// <returns>Vrai si le texte fait 64 caracteres de <c>0</c> a <c>f</c>.</returns>
    public static bool IsLowerHex64(string? text)
    {
        if (text is null || text.Length != HexLength)
        {
            return false;
        }

        foreach (char c in text)
        {
            bool ok = c is >= '0' and <= '9' or >= 'a' and <= 'f';
            if (!ok)
            {
                return false;
            }
        }

        return true;
    }
}
