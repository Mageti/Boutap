// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Globalization;

using Boutap.Core.Common;
using Boutap.Core.Pack;

namespace Boutap.Game.Library;

/// <summary>Un niveau d'un pack, tel que le joueur le choisit.</summary>
/// <param name="Level">Niveau de difficulté.</param>
/// <param name="File">Entrée du pack qui contient la partition.</param>
/// <param name="NoteCount">Nombre de notes annoncé par le manifeste.</param>
/// <param name="Rating">Note de difficulté, si le manifeste en porte une.</param>
/// <param name="Nps">Notes par seconde de crête.</param>
public sealed record LibraryLevel(
    ChartLevel Level,
    string File,
    int NoteCount,
    int? Rating,
    double? Nps);

/// <summary>Un pack de la bibliothèque, jouable ou refusé.</summary>
/// <remarks>
/// <para>
/// Un pack illisible reste listé, avec sa raison d'être refusé. Le joueur qui
/// dépose un fichier dans le dossier doit apprendre pourquoi rien ne part, pas
/// découvrir une liste vide qui a l'air d'un programme cassé.
/// </para>
/// <para>
/// Rien n'est gardé ouvert : <see cref="LoadedPack"/> tient une archive ZIP, et
/// une bibliothèque de cent morceaux ne peut pas garder cent descripteurs de
/// fichier. La liste ne retient que ce que le manifeste dit ; le pack est rouvert
/// au moment de jouer.
/// </para>
/// </remarks>
public sealed record LibraryEntry(
    string Path,
    string FileName,
    string Id,
    string Title,
    string Artist,
    double? DurationSeconds,
    double? Bpm,
    KeySignature? Key,
    IReadOnlyList<LibraryLevel> Levels,
    string? Error)
{
    /// <summary>Vrai si le pack peut être joué.</summary>
    public bool IsPlayable => Error is null && Levels.Count > 0;

    /// <summary>Ce que le joueur lit dans la liste.</summary>
    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? FileName : Title;

    /// <summary>Artiste et durée formatés, ou une chaîne vide.</summary>
    public string DisplaySubtitle
    {
        get
        {
            var parts = new List<string>(2);
            if (!string.IsNullOrWhiteSpace(Artist))
            {
                parts.Add(Artist);
            }

            if (DurationSeconds is double seconds && seconds > 0)
            {
                parts.Add(FormatDuration(seconds));
            }

            return string.Join("  ·  ", parts);
        }
    }

    /// <summary>Durée en minutes et secondes, arrondie à la seconde.</summary>
    public static string FormatDuration(double seconds)
    {
        double clamped = seconds < 0 ? 0 : seconds;
        int total = (int)Math.Round(clamped, MidpointRounding.AwayFromZero);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{total / 60}:{total % 60:00}");
    }
}

/// <summary>La bibliothèque de morceaux, indifférente au moteur.</summary>
/// <remarks>
/// Un dossier, des fichiers <c>.btp</c>, une liste triée. Aucune horloge, aucun
/// rendu : la coque demande une liste, la coque l'affiche.
/// </remarks>
public static class PackLibrary
{
    /// <summary>Extension d'un pack, en minuscules.</summary>
    public const string PackExtension = ".btp";

    /// <summary>Lit tous les packs d'un dossier, triés.</summary>
    /// <param name="directory">Dossier à balayer.</param>
    /// <returns>
    /// Les packs trouvés, triés par titre puis par chemin. Un dossier absent
    /// rend une liste vide : un premier lancement n'est pas une erreur.
    /// </returns>
    public static IReadOnlyList<LibraryEntry> Scan(string directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var found = new List<LibraryEntry>();
        foreach (string path in Files(directory))
        {
            found.Add(Load(path));
        }

        found.Sort(CompareEntries);
        return found;
    }

    /// <summary>Lit un pack seul, sans jamais lever d'exception.</summary>
    /// <param name="path">Chemin du pack.</param>
    public static LibraryEntry Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        string fileName = Path.GetFileName(path);
        try
        {
            using LoadedPack pack = PackReader.Read(path);
            return FromLoaded(pack);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or System.Security.SecurityException or InvalidDataException
            or System.Text.Json.JsonException or NotSupportedException
            or PackFormatException)
        {
            // Un pack déposé là par erreur ne doit pas empêcher les autres de
            // s'afficher, ni planter le jeu au démarrage.
            return new LibraryEntry(
                path,
                fileName,
                string.Empty,
                fileName,
                string.Empty,
                DurationSeconds: null,
                Bpm: null,
                Key: null,
                Levels: [],
                Error: Reason(ex));
        }
    }

    /// <summary>Construit une entrée depuis un pack déjà ouvert.</summary>
    /// <param name="pack">Pack ouvert par l'appelant, qui le refermera.</param>
    public static LibraryEntry FromLoaded(LoadedPack pack)
    {
        ArgumentNullException.ThrowIfNull(pack);

        Manifest manifest = pack.Manifest;
        var levels = new List<LibraryLevel>(manifest.Charts?.Count ?? 0);
        foreach (ChartEntry entry in manifest.Charts ?? [])
        {
            if (entry.File is not { Length: > 0 } file)
            {
                continue;
            }

            levels.Add(new LibraryLevel(
                entry.Level ?? ChartLevel.Ronde,
                file,
                entry.NoteCount ?? 0,
                entry.Rating,
                entry.Nps));
        }

        levels.Sort(static (left, right) => left.Level.CompareTo(right.Level));

        return new LibraryEntry(
            pack.Path,
            Path.GetFileName(pack.Path),
            manifest.Id ?? string.Empty,
            manifest.Title ?? string.Empty,
            manifest.Artist ?? string.Empty,
            manifest.Audio?.DurationSeconds,
            manifest.Analysis?.Bpm,
            manifest.Analysis?.Key,
            levels,
            Error: null);
    }

    /// <summary>Vrai si le chemin a l'extension d'un pack.</summary>
    /// <param name="path">Chemin à examiner.</param>
    public static bool IsPackPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Path.GetExtension(path)
            .Equals(PackExtension, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Fichiers du dossier, tous les packs quelle que soit la casse de
    /// l'extension : Windows ignore la casse, Linux non, et la bibliothèque doit
    /// montrer la même chose des deux côtés.
    /// </summary>
    private static List<string> Files(string directory)
    {
        string[] everything;
        try
        {
            everything = Directory.GetFiles(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        var kept = new List<string>();
        foreach (string path in everything)
        {
            if (IsPackPath(path))
            {
                kept.Add(path);
            }
        }

        return kept;
    }

    /// <summary>Ordre stable et sans culture, pour que la liste ne saute pas.</summary>
    private static int CompareEntries(LibraryEntry left, LibraryEntry right)
    {
        int byTitle = string.CompareOrdinal(left.DisplayTitle, right.DisplayTitle);
        return byTitle != 0 ? byTitle : string.CompareOrdinal(left.Path, right.Path);
    }

    private static string Reason(Exception ex) => ex switch
    {
        PackFormatException => "ce fichier n'est pas un pack Boutap valide",
        InvalidDataException => "ce fichier n'est pas un pack lisible",
        System.Text.Json.JsonException => "le manifeste de ce pack est illisible",
        FileNotFoundException => "ce fichier a disparu",
        UnauthorizedAccessException => "accès refusé à ce fichier",
        _ => "ce pack n'a pas pu être lu",
    };
}
