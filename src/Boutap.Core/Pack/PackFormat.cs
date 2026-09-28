// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Boutap.Core.Pack;

/// <summary>
/// Les constantes du format de pack. Elles sont ici, et nulle part ailleurs :
/// un format se discute, il ne se redefinit pas au fil des fichiers.
/// </summary>
/// <remarks>
/// See wiki: spec.md §6 et wiki: format.md.
/// </remarks>
public static class PackFormat
{
    /// <summary>Valeur du champ <c>schema</c> du manifeste.</summary>
    public const string ManifestSchema = "boutap/pack-manifest/1";

    /// <summary>Valeur du champ <c>schema</c> d'une chart.</summary>
    public const string ChartSchema = "boutap/chart/1";

    /// <summary>Nom du manifeste dans l'archive.</summary>
    public const string ManifestFileName = "manifest.json";

    /// <summary>Repertoire des charts dans l'archive.</summary>
    public const string ChartsDirectory = "charts";

    /// <summary>
    /// Rapport de generation. N'est pas declare dans le manifeste : le
    /// manifeste est <c>additionalProperties: false</c>, et y ajouter un champ
    /// deplus serait un changement de format. Le fichier est donc une
    /// convention de nom, comme <see cref="LicenseFileName"/>.
    /// </summary>
    public const string ReportFileName = "REPORT.txt";

    /// <summary>Copie de la licence du contenu, par convention de nom.</summary>
    public const string LicenseFileName = "LICENSE.txt";

    /// <summary>Prefixe impose aux noms de fichier de chart.</summary>
    public const string ChartFilePrefix = ChartsDirectory + "/";

    /// <summary>Extension des fichiers de chart.</summary>
    public const string ChartFileExtension = ".json";

    /// <summary>Extension des packs.</summary>
    public const string PackExtension = ".btp";

    /// <summary>Force par defaut d'une note (<c>v</c>).</summary>
    public const int DefaultForce = 8;

    /// <summary>Multiplicateur de fenetre par defaut (<c>w</c>).</summary>
    public const double DefaultWindowScale = 1;

    /// <summary>Decalage de phase par defaut (<c>s</c>).</summary>
    public const double DefaultPhaseShift = 0;

    /// <summary>Duree maximale d'une tenue (<c>d</c>), en secondes.</summary>
    public const double MaxHoldSeconds = 10;

    /// <summary>Temps maximal d'une note, en secondes.</summary>
    public const double MaxNoteTimeSeconds = 3600.5;

    /// <summary>Nombre maximum de notes dans une chart.</summary>
    public const int MaxNotes = 100_000;

    /// <summary>
    /// Tolerance entre <c>analysis.duration_seconds</c> et
    /// <c>audio.duration_seconds</c>. Le schema dit 5 ms ; on rejoue exactement
    /// cette valeur plutot que d'en inventer une plus commode.
    /// </summary>
    public const double DurationToleranceSeconds = 0.005;

    /// <summary>
    /// Regle R2 du format : le contenu d'un pack ne sort jamais du pack.
    /// </summary>
    public const string NoPathEscapeRule =
        "Un chemin de pack est relatif et ne peut contenir ni « .. » ni racine absolue.";

    /// <summary>Forme du nom de fichier de chart, telle que le schema l'impose.</summary>
    public const string ChartFilePattern = "charts/[a-z0-9_-]+\\.json";

    /// <summary>Retourne le nom de fichier de chart canonique d'un niveau.</summary>
    public static string ChartFileName(ChartLevel level) =>
        ChartFilePrefix + ChartLevels.FileNameOf(level) + ChartFileExtension;
}
