// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.IO.Compression;

namespace Boutap.Core.Pack;

/// <summary>Un niveau annonce par le manifeste, et ce qu'on a reussi a en lire.</summary>
public sealed record LoadedChart
{
    /// <summary>L'entree du manifeste.</summary>
    public required ChartEntry Entry { get; init; }

    /// <summary>Chemin du fichier, tel qu'annonce.</summary>
    public string? File { get; init; }

    /// <summary>Texte brut du fichier, s'il existe.</summary>
    public string? Json { get; init; }

    /// <summary>La chart analysee, si elle a pu l'etre.</summary>
    public Chart? Chart { get; init; }

    /// <summary>Ce qui a empeche la lecture, si elle a echoue.</summary>
    public string? ReadError { get; init; }

    /// <summary>Indique si la chart a pu etre analysee.</summary>
    public bool IsReadable => Chart is not null && ReadError is null;

    /// <summary>Indique si le fichier annonce est present dans le pack.</summary>
    public bool FileExists => Json is not null;
}

/// <summary>
/// Un pack lu et compris : le manifeste, les charts qu'il annonce, et un moyen
/// d'ouvrir les autres fichiers de l'archive a la demande.
/// </summary>
/// <remarks>
/// <para>
/// L'archive reste ouverte. Un pack contient un audio, et un audio de six
/// minutes en flottants 32 bits occupe presque 300 Mo : le copier en memoire
/// pour pouvoir le relire serait le seul moyen de ne pas pouvoir ouvrir un
/// pack de taille normale. Le validateur lit donc l'audio en flux, et ne le
/// materialise que s'il a reellement besoin de le decoder.
/// </para>
/// <para>
/// Le texte brut du manifeste et des charts est conserve. Le schema interdit
/// tout champ inconnu, et verifier cela demande de comparer les cles du
/// document aux cles du modele — ce que le modele, une fois deserialise, ne
/// peut plus dire.
/// </para>
/// </remarks>
public sealed class LoadedPack : IDisposable
{
    private readonly ZipArchive _archive;
    private readonly Stream? _backing;
    private bool _disposed;

    /// <summary>Cree un pack deja ouvert.</summary>
    /// <param name="path">Chemin du fichier <c>.btp</c>.</param>
    /// <param name="archive">Archive ouverte, dont la propriete est reprise par ce pack.</param>
    /// <param name="backing">
    /// Flux sur lequel l'archive est ouverte, dont la propriete est reprise par
    /// ce pack. L'archive garde la main sur le fichier tant qu'elle n'est pas
    /// fermee : un audio peut encore etre lu apres coup, donc le fichier ne
    /// peut pas etre libere au retour de la lecture.
    /// </param>
    /// <param name="manifest">Manifeste analyse.</param>
    /// <param name="manifestJson">Texte brut du manifeste.</param>
    /// <param name="charts">Niveaux annonces.</param>
    /// <param name="entries">Noms de toutes les entrees de l'archive.</param>
    public LoadedPack(
        string path,
        ZipArchive archive,
        Stream? backing,
        Manifest manifest,
        string manifestJson,
        IReadOnlyList<LoadedChart> charts,
        IReadOnlyList<string> entries)
    {
        Path = path;
        _archive = archive;
        _backing = backing;
        Manifest = manifest;
        ManifestJson = manifestJson;
        Charts = charts;
        Entries = entries;
    }

    /// <summary>Chemin du fichier lu.</summary>
    public string Path { get; }

    /// <summary>Manifeste analyse.</summary>
    public Manifest Manifest { get; }

    /// <summary>Texte brut du manifeste, pour la verification des champs inconnu.</summary>
    public string ManifestJson { get; }

    /// <summary>Niveaux annonces, dans l'ordre du manifeste.</summary>
    public IReadOnlyList<LoadedChart> Charts { get; }

    /// <summary>Toutes les entrees de l'archive, chemins separes par <c>/</c>.</summary>
    public IReadOnlyList<string> Entries { get; }

    /// <summary>Indique si l'archive contient une entree de ce nom.</summary>
    public bool HasEntry(string entryName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _archive.GetEntry(entryName) is not null;
    }

    /// <summary>Ouvre une entree en lecture, ou renvoie null si elle n'existe pas.</summary>
    public Stream? OpenEntry(string entryName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _archive.GetEntry(entryName)?.Open();
    }

    /// <summary>Libere l'archive.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // L'ordre compte : l'archive se sert encore du flux en se fermant.
        _archive.Dispose();
        _backing?.Dispose();
    }
}
