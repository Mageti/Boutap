// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Reconnaissance des balises gapless d'un WAV. Le manifeste doit dire d'ou
// vient la longueur annoncee, sinon le lecteur ne peut pas prevoir la fin.

using System.Buffers.Binary;

using Boutap.Core.Pack;

namespace Boutap.Audio;

/// <summary>Cherche d'ou vient la longueur annoncee d'un WAV.</summary>
/// <remarks>
/// Trois familles se rencontrent, et elles ne se ressemblent pas :
/// <list type="bullet">
/// <item><description>
/// <b>Xing</b> et <b>Info</b> : un mot de quatre octets au tout debut du chunk
/// <c>data</c>. <c>Xing</c> est ecrit par les encodeurs CBR, <c>Info</c> par
/// les encodeurs VBR, et les deux signifient la meme chose.
/// </description></item>
/// <item><description>
/// <b>LAME</b> : chaine de caracteres dans un chunk <c>ID3 </c> ou une liste
/// <c>INFO</c>. Elle ne porte aucun compteur de longueur, donc elle ne dit rien
/// de la fin du morceau : elle ne compte pas comme balise gapless.
/// </description></item>
/// <item><description>
/// <b>VBRI</b> : a l'octet 32 du chunk <c>data</c>, quelle que soit sa taille.
/// Convention de Fraunhofer.
/// </description></item>
/// <item><description>
/// <b>iTunSMPB</b> : la ou iTunes la place, sous forme de chaine. Elle encode
/// la longueur en millisecondes, pas en echantillons, et son endianness a
/// change deux fois : c'est la seule qu'on ne pretend pas lire.
/// </description></item>
/// </list>
/// Ce qui n'est pas reconnu vaut <see cref="GaplessTag.Unknown"/> : c'est un
/// constat, pas une erreur. Un WAV sans balise se joue tres bien, on ne peut
/// simplement pas anticiper sa fin.
/// </remarks>
public static class WavGapless
{
    private const int MinimumHeaderBytes = 12;

    /// <summary>Cherche la balise gapless d'un WAV entier.</summary>
    /// <param name="wav">Le fichier complet, en-tete compris.</param>
    public static GaplessTag Read(ReadOnlySpan<byte> wav)
    {
        if (wav.Length < MinimumHeaderBytes
            || !wav[..4].SequenceEqual("RIFF"u8)
            || !wav.Slice(8, 4).SequenceEqual("WAVE"u8))
        {
            return GaplessTag.Unknown;
        }

        int position = MinimumHeaderBytes;
        while (position + 8 <= wav.Length)
        {
            ReadOnlySpan<byte> id = wav.Slice(position, 4);
            long size = BinaryPrimitives.ReadUInt32LittleEndian(wav.Slice(position + 4, 4));
            long body = position + 8;
            if (size < 0 || body + size > wav.Length)
            {
                return GaplessTag.Unknown;
            }

            ReadOnlySpan<byte> content = wav[(int)body..(int)(body + size)];

            if (id.SequenceEqual("data"u8))
            {
                GaplessTag fromData = FromDataChunk(content);
                if (fromData != GaplessTag.Unknown)
                {
                    return fromData;
                }
            }
            // LAME ecrit aussi son nom dans un chunk ID3 ou une liste INFO.
            // On ne peut rien en deduire sur la duree, donc on ne s'en occupe
            // pas : mieux vaut « unknown » honnete qu'un « xing » invente.
            else if (ContainsAscii(content, "iTunSMPB"u8))
            {
                return GaplessTag.ITunSmpb;
            }

            // Les chunks ont une taille impaire : un octet de bourrage suit.
            position = (int)(body + size + (size % 2));
        }

        return GaplessTag.Unknown;
    }

    private static GaplessTag FromDataChunk(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 4)
        {
            if (data[..4].SequenceEqual("Xing"u8) || data[..4].SequenceEqual("Info"u8))
            {
                return GaplessTag.Xing;
            }
        }

        // VBRI : l'identifiant est precede de 32 octets de version, framerate
        // et infos de tables. On lit donc en deca, pas en qua.
        if (data.Length >= 36 && data.Slice(32, 4).SequenceEqual("VBRI"u8))
        {
            return GaplessTag.Vbri;
        }

        return GaplessTag.Unknown;
    }

    private static bool ContainsAscii(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle)
    {
        return haystack.IndexOf(needle) >= 0;
    }

    /// <summary>Le nom de la source, tel que le manifeste l'ecrit.</summary>
    /// <param name="tag">La balise reconnue.</param>
    public static string Describe(GaplessTag tag)
    {
        return tag switch
        {
            GaplessTag.Xing => "Xing (LAME) ou Info, en tete du chunk de donnees",
            GaplessTag.ITunSmpb => "iTunSMPB, en texte dans un chunk annexe",
            GaplessTag.Vbri => "VBRI, a l'octet 32 du chunk de donnees",
            GaplessTag.None => "aucune balise : la fin du morceau ne peut pas etre prevue",
            _ => "une balise est presente, mais on ne sait pas la lire",
        };
    }
}
