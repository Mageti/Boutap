// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Boutap.Core.Common;
using Boutap.Core.Pack;

namespace Boutap.Audio;

/// <summary>
/// Mesure un audio de pack : SHA-256 de la forme <em>decodee</em> et duree.
/// </summary>
/// <remarks>
/// <para>
/// Le manifeste ne hache pas le fichier tel qu'il est stocke, mais sa forme
/// canonique — flottants 32 bits, entrelaces, [-1 ; 1] — parce que c'est cette
/// forme que le joueur.decode reellement. Un WAV 16 bits et le meme signal en
/// flottants decrivent la meme musique : ils doivent avoir le meme hash.
/// </para>
/// <para>
/// L'audio d'un pack arrive par une entree de
/// <see cref="System.IO.Compression.ZipArchive"/>, et ces entrees ne sont pas
/// seekable. Le decodage travaille donc en avant seulement, et le hachage se
/// fait bloc par bloc : un morceau long ne doit pas etre charge en memoire
/// entiere pour etre valide.
/// </para>
/// </remarks>
public sealed class WavAudioProbe : IPackAudioProbe
{
    /// <inheritdoc/>
    public bool TryInspect(Stream audio, out PackAudioFacts facts)
    {
        ArgumentNullException.ThrowIfNull(audio);

        facts = default;

        WavInfo info;
        try
        {
            info = WavDecoder.ReadInfo(audio);
        }
        catch (Exception ex) when (ex is WavFormatException or IOException or NotSupportedException)
        {
            // Un audio illisible est un constat, pas une panne : la validation
            // doit pouvoir continuer et rapporter tous les autres problemes du
            // meme pack.
            return false;
        }

        try
        {
            string canonicalSha256 = WavDecoder.HashCanonicalToHex(audio, info, out _);
            facts = new PackAudioFacts(
                canonicalSha256,
                info.DurationSeconds,
                info.SampleRate,
                info.Channels,
                info.BitsPerSample);
        }
        catch (Exception ex) when (ex is WavFormatException or IOException or NotSupportedException)
        {
            return false;
        }

        return true;
    }

    /// <summary>Version de la sonde, pour <c>audit</c> et les rapports.</summary>
    public static string Name => "wav";

    /// <summary>
    /// Forme canonique, en toutes lettres. Ce texte doit rester identique a la
    /// description de <c>schema/manifest.schema.json</c> et a
    /// <c>format.md</c>.
    /// </summary>
    public static string CanonicalFormDescription =>
        "flottants 32 bits IEEE entrelaces dans [-1 ; 1], ordre des canaux d'origine";
}
