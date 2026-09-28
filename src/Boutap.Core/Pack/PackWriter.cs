// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.IO.Compression;
using System.Text;

namespace Boutap.Core.Pack;

/// <summary>Tout ce qu'un pack contient, pret a etre ecrit.</summary>
public sealed record PackContent
{
    /// <summary>Le manifeste.</summary>
    public required Manifest Manifest { get; init; }

    /// <summary>Les charts, dans l'ordre du manifeste.</summary>
    public required IReadOnlyList<Chart> Charts { get; init; }

    /// <summary>Chemin de l'audio dans le pack, forme <c>musique/xxx.wav</c>.</summary>
    public required string AudioPath { get; init; }

    /// <summary>Octets de l'audio, dans la forme stockee.</summary>
    public required byte[] AudioBytes { get; init; }

    /// <summary>Rapport de generation, ecrit en <c>REPORT.txt</c> s'il est fourni.</summary>
    public string? Report { get; init; }

    /// <summary>Texte de la licence de contenu, ecrit en <c>LICENSE.txt</c>.</summary>
    public string? LicenseText { get; init; }
}

/// <summary>
/// Ecrit un pack <c>.btp</c>. Deux executions avec le meme contenu produisent
/// le meme fichier, octet pour octet.
/// </summary>
/// <remarks>
/// <para>
/// Le determinisme n'est pas une coquetterie : le generateur est destine a
/// etre relance sur une autre machine pour verifier qu'il donne le meme
/// resultat. Un pack dont la date d'ecriture differe ne peut pas etre compare
/// fichier a fichier, et le controle « la meme graine donne le meme pack »
/// devient une comparaison de JSON a la main.
/// </para>
/// <para>
/// L'ordre des entrees est fixe — manifeste, audio, charts par niveau, puis
/// <c>REPORT.txt</c> et <c>LICENSE.txt</c> — parce que <see cref="ZipArchive"/>
/// ne garantit rien sur l'ordre de parcours.
/// </para>
/// </remarks>
public static class PackWriter
{
    /// <summary>
    /// Date d'ecriture de toutes les entrees. 1980 est la date plancher du
    /// format ZIP : c'est une constante de format, pas l'heure du systeme, et
    /// la regle R1 n'a rien a y redire.
    /// </summary>
    public static DateTimeOffset EntryTimestamp { get; } = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Nom du fichier temporaire ecrit avant le remplacement.</summary>
    public const string TemporarySuffix = ".btp.tmp";

    /// <summary>Ecrit un pack.</summary>
    /// <param name="path">Chemin du fichier <c>.btp</c> a ecrire.</param>
    /// <param name="content">Contenu du pack.</param>
    /// <returns>Le nombre d'octets ecrits.</returns>
    public static long Write(string path, PackContent content)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(content);
        ValidateEntryPath(content.AudioPath);

        string full = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temporary = full + TemporarySuffix;
        long written;
        using (FileStream file = new(
            temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            written = WriteTo(file, content);
        }

        // Remplacer d'un coup : un pack interrompu ne doit jamais remplacer un
        // pack valide.
        File.Move(temporary, full, overwrite: true);
        return written;
    }

    /// <summary>Ecrit un pack dans un flux deja ouvert.</summary>
    /// <param name="stream">Flux de destination.</param>
    /// <param name="content">Contenu du pack.</param>
    /// <returns>Le nombre d'octets ecrits.</returns>
    public static long WriteTo(Stream stream, PackContent content)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(content);

        using ZipArchive archive = new(stream, ZipArchiveMode.Create, leaveOpen: true);

        WriteText(archive, PackFormat.ManifestFileName, PackJson.Write(content.Manifest) + "\n");
        WriteBytes(archive, content.AudioPath, content.AudioBytes);

        foreach (Chart chart in content.Charts)
        {
            // Sans niveau, on ne peut pas nommer le fichier. Cela ne peut pas
            // arriver : le manifeste l'exige, donc le validateur l'a refuse
            // avant qu'on n'arrive ici.
            string name = chart.Level is { } level
                ? PackFormat.ChartFileName(level)
                : PackFormat.ChartFilePrefix + "chart.json";
            WriteText(archive, name, PackJson.Write(chart) + "\n");
        }

        if (content.Report is not null)
        {
            WriteText(archive, PackFormat.ReportFileName, content.Report);
        }

        if (content.LicenseText is not null)
        {
            WriteText(archive, PackFormat.LicenseFileName, content.LicenseText);
        }

        return stream.CanSeek ? stream.Position : 0;
    }

    private static void WriteText(ZipArchive archive, string name, string text)
    {
        WriteBytes(archive, name, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text));
    }

    private static void WriteBytes(ZipArchive archive, string name, byte[] bytes)
    {
        ValidateEntryPath(name);
        ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        entry.LastWriteTime = EntryTimestamp;
        using Stream stream = entry.Open();
        stream.Write(bytes, 0, bytes.Length);
    }

    /// <summary>
    /// Refuse un chemin d'entree qui sortirait du pack. C'est la regle R2 du
    /// format, appliquee a l'ecriture : un generateur qui produit un chemin
    /// douteux doit echouer ici, pas chez celui qui decompressera.
    /// </summary>
    private static void ValidateEntryPath(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new PackFormatException("Un chemin d'entree de pack ne peut pas etre vide.");
        }

        if (name.StartsWith('/') || name.Contains('\\', StringComparison.Ordinal))
        {
            throw new PackFormatException(
                $"« {name} » n'est pas un chemin de pack valide : {PackFormat.NoPathEscapeRule}");
        }

        if (name.Contains("..", StringComparison.Ordinal))
        {
            throw new PackFormatException(
                $"« {name} » n'est pas un chemin de pack valide : {PackFormat.NoPathEscapeRule}");
        }
    }
}
