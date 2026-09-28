// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.IO.Compression;

namespace Boutap.Core.Pack;

/// <summary>
/// Ouvre un pack <c>.btp</c>. Un pack est une archive ZIP : ni chiffrement, ni
/// compression exotique, ni repertoire imbrique au-dela de <c>charts/</c>.
/// </summary>
/// <remarks>
/// Ce lecteur ne juge pas : il dit « fichier illisible » ou il renvoie un
/// <see cref="LoadedPack"/>. Les regles de forme et de coherence sont le
/// travail de <see cref="PackValidator"/>, pour qu'un rapport de validation
/// puisse contenir tous les problemes d'un coup.
/// </remarks>
public static class PackReader
{
    /// <summary>Taille maximale d'une entree non audio, en octets.</summary>
    public const long MaxTextEntryBytes = 64L * 1024 * 1024;

    /// <summary>Taille maximale de l'audio, en octets. Au-dela, on lit par flux.</summary>
    public const long MaxAudioEntryBytes = 2L * 1024 * 1024 * 1024;

    /// <summary>Lit un pack depuis le disque.</summary>
    /// <param name="path">Chemin du fichier <c>.btp</c>.</param>
    /// <returns>Le pack ouvert. L'appelant en est proprietaire et doit le fermer.</returns>
    /// <exception cref="PackFormatException">Le fichier n'est pas un pack lisible.</exception>
    public static LoadedPack Read(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        if (!File.Exists(path))
        {
            throw new PackFormatException($"« {path} » n'existe pas.");
        }

        FileStream file;
        try
        {
            file = File.OpenRead(path);
        }
        catch (IOException ex)
        {
            throw new PackFormatException($"« {path} » ne peut pas etre lu : {ex.Message}", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new PackFormatException($"« {path} » ne peut pas etre lu : acces refuse.", ex);
        }

        ZipArchive archive;
        try
        {
            // leaveOpen: true, parce que l'audio est lu plus tard, apres le
            // retour de cette methode. C'est LoadedPack qui libere le fichier,
            // et dans le bon ordre.
            archive = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException ex)
        {
            file.Dispose();
            throw new PackFormatException(
                $"« {Path.GetFileName(path)} » n'est pas une archive ZIP : {ex.Message}", ex);
        }

        try
        {
            return ReadFrom(path, archive, file);
        }
        catch
        {
            archive.Dispose();
            file.Dispose();
            throw;
        }
    }

    private static LoadedPack ReadFrom(string path, ZipArchive archive, Stream backing)
    {
        List<string> entries = new();
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            entries.Add(Normalize(entry.FullName));
        }

        if (entries.Count == 0)
        {
            throw new PackFormatException(
                $"« {Path.GetFileName(path)} » ne contient aucun fichier.");
        }

        ZipArchiveEntry? manifestEntry = archive.GetEntry(PackFormat.ManifestFileName);
        if (manifestEntry is null)
        {
            throw new PackFormatException(
                $"« {Path.GetFileName(path)} » ne contient pas « {PackFormat.ManifestFileName} ». "
                + $"Il contient : {string.Join(", ", entries.Take(8))}.");
        }

        string manifestJson = ReadText(manifestEntry, PackFormat.ManifestFileName);
        Manifest manifest = PackJson.Read<Manifest>(manifestJson, PackFormat.ManifestFileName);

        List<LoadedChart> charts = new();
        if (manifest.Charts is not null)
        {
            foreach (ChartEntry entry in manifest.Charts)
            {
                charts.Add(ReadChart(archive, entry));
            }
        }

        return new LoadedPack(path, archive, backing, manifest, manifestJson, charts, entries);
    }

    /// <summary>
    /// Lit un niveau. Une chart illisible n'est pas fatale : le reste du pack
    /// peut encore etre verifie, et un rapport qui s'arrete au premier fichier
    /// illisible oblige a autant d'executions que de fichiers.
    /// </summary>
    private static LoadedChart ReadChart(ZipArchive archive, ChartEntry entry)
    {
        string? file = entry.File;
        if (string.IsNullOrEmpty(file))
        {
            return new LoadedChart
            {
                Entry = entry,
                ReadError = "Le manifeste n'indique pas de fichier pour ce niveau.",
            };
        }

        ZipArchiveEntry? chartEntry = archive.GetEntry(file);
        if (chartEntry is null)
        {
            return new LoadedChart
            {
                Entry = entry,
                File = file,
                ReadError = "Le fichier annonce n'est pas dans le pack.",
            };
        }

        string json;
        try
        {
            json = ReadText(chartEntry, file);
        }
        catch (PackFormatException ex)
        {
            return new LoadedChart { Entry = entry, File = file, ReadError = ex.Message };
        }

        try
        {
            return new LoadedChart
            {
                Entry = entry,
                File = file,
                Json = json,
                Chart = PackJson.Read<Chart>(json, file),
            };
        }
        catch (PackFormatException ex)
        {
            return new LoadedChart { Entry = entry, File = file, Json = json, ReadError = ex.Message };
        }
    }

    private static string ReadText(ZipArchiveEntry entry, string what)
    {
        if (entry.Length > MaxTextEntryBytes)
        {
            throw new PackFormatException(
                $"« {what} » fait {entry.Length} octets, au-dela de la limite de "
                + $"{MaxTextEntryBytes} octets pour un fichier texte.");
        }

        using Stream stream = entry.Open();
        using StreamReader reader = new(stream, detectEncodingFromByteOrderMarks: false);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Ramene un chemin d'archive a la forme qu'attend le format : separateurs
    /// <c>/</c>, pas d'esperance de repertoire.
    /// </summary>
    private static string Normalize(string fullName) => fullName.Replace('\\', '/').TrimStart('/');
}
