// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
//
// L'identifiant d'un pack : il doit etre stable et il ne doit dependre
// ni du systeme de fichiers, ni de l'heure.

using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Boutap.Gen.Cli;

/// <summary>Fabrique l'identifiant d'un pack a partir du nom du fichier.</summary>
/// <remarks>
/// Le manifeste exige <c>^[a-z0-9][a-z0-9_-]{2,63}$</c>. Un nom de fichier
/// contient des espaces, des accents et parfois des points, on le replie donc.
/// Si le nom ne laisse rien, on tombe sur une empreinte du chemin : deux
/// machines qui nomment le fichier autrement obtiennent deux packs differents,
/// ce qui est honnete puisque le contenu, lui, n'a pas change.
/// </remarks>
internal static class Identifier
{
    /// <summary>Replie un nom de fichier en identifiant.</summary>
    /// <param name="path">Le chemin du fichier audio.</param>
    public static string FromPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var builder = new StringBuilder(64);
        foreach (char raw in Path.GetFileNameWithoutExtension(path).Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(raw) != UnicodeCategory.NonSpacingMark)
            {
                Append(builder, raw);
            }
        }

        if (builder.Length >= 3)
        {
            return builder.ToString();
        }

        return "pack-" + Sha(path);
    }

    private static void Append(StringBuilder builder, char raw)
    {
        char c = char.ToLowerInvariant(raw);
        if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' || c == '-')
        {
            builder.Append(c);
        }
        else if (builder.Length > 0 && builder[^1] != '-')
        {
            builder.Append('-');
        }

        if (builder.Length == 64)
        {
            return;
        }
    }

    private static string Sha(string path)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(path)));
        return Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
    }
}
