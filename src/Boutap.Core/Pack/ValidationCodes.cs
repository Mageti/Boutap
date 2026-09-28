// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Boutap.Core.Pack;

/// <summary>
/// Les codes de constat, ecrits ici plutot qu'en dur dans le validateur.
/// </summary>
/// <remarks>
/// Un code est une promesse : s'il apparait dans un rapport, il veut dire la
/// meme chose dans la version qui suit. Le renommer casse les scripts qui
/// filtrent dessus, donc on en ajoute plutot qu'on ne remplace.
/// </remarks>
public static class ValidationCodes
{
    // --- L'archive ---------------------------------------------------------
    /// <summary>Le fichier n'est pas une archive ZIP lisible.</summary>
    public const string PackNotAnArchive = "pack.not-an-archive";

    /// <summary>L'archive ne contient pas de manifeste.</summary>
    public const string PackNoManifest = "pack.no-manifest";

    /// <summary>L'archive ne contient aucun fichier du tout.</summary>
    public const string PackEmpty = "pack.empty";

    /// <summary>Une entree de l'archive sort du pack (zip slip).</summary>
    public const string PackEntryEscape = "pack.entry-escape";

    /// <summary>Le manifeste declare une audio que le pack ne contient pas.</summary>
    public const string AudioMissing = "audio.missing";

    /// <summary>Le contenu de l'audio ne correspond pas au SHA-256 annonce.</summary>
    public const string AudioHashMismatch = "audio.hash-mismatch";

    /// <summary>L'audio n'a pas pu etre decode.</summary>
    public const string AudioUnreadable = "audio.unreadable";

    // --- Le manifeste ------------------------------------------------------
    /// <summary>Un champ obligatoire est absent.</summary>
    public const string MissingField = "manifest.missing-field";

    /// <summary>Un champ present n'existe pas dans le format.</summary>
    public const string UnknownField = "manifest.unknown-field";

    /// <summary>La valeur d'un champ est hors bornes ou mal formee.</summary>
    public const string BadValue = "manifest.bad-value";

    /// <summary>La version de schema n'est pas celle que ce lecteur connait.</summary>
    public const string SchemaUnknown = "manifest.schema-unknown";

    /// <summary>La licence de contenu n'est pas dans la liste blanche.</summary>
    public const string LicenseNotAllowed = "manifest.license-not-allowed";

    /// <summary>
    /// La licence est acceptee, mais elle impose plus de conditions que le
    /// reste de la liste blanche.
    /// </summary>
    public const string LicenseRequiresShareAlike = "manifest.license-requires-share-alike";

    /// <summary>Deux niveaux portent le meme nom.</summary>
    public const string DuplicateLevel = "manifest.duplicate-level";

    /// <summary>Le nombre de notes annonce ne correspond pas au fichier.</summary>
    public const string NoteCountMismatch = "manifest.note-count-mismatch";

    /// <summary>La duree mesuree et la duree de l'audio divergent de plus de 5 ms.</summary>
    public const string DurationMismatch = "manifest.duration-mismatch";

    /// <summary>La graine ne derive pas du contenu de l'audio.</summary>
    public const string SeedNotDerived = "manifest.seed-not-derived";

    /// <summary>Le pack exige un lecteur plus recent que celui-ci.</summary>
    public const string PlayerTooOld = "manifest.player-too-old";

    /// <summary>Le rapport de generation est absent, alors que la convention le demande.</summary>
    public const string ReportMissing = "manifest.report-missing";

    /// <summary>La copie de la licence de contenu est absente.</summary>
    public const string LicenseFileMissing = "manifest.license-file-missing";

    /// <summary>La copie de la licence de contenu ne reprend pas l'identifiant annonce.</summary>
    public const string LicenseFileMismatch = "manifest.license-file-mismatch";

    // --- Une chart ---------------------------------------------------------
    /// <summary>La chart ne peut pas etre lue comme du JSON.</summary>
    public const string ChartNotJson = "chart.not-json";

    /// <summary>Un champ obligatoire de chart est absent.</summary>
    public const string ChartMissingField = "chart.missing-field";

    /// <summary>Un champ present de chart n'existe pas dans le format.</summary>
    public const string ChartUnknownField = "chart.unknown-field";

    /// <summary>La version de schema de la chart n'est pas connue.</summary>
    public const string ChartSchemaUnknown = "chart.schema-unknown";

    /// <summary>La chart annonce un niveau que le manifeste ne lui attribue pas.</summary>
    public const string ChartLevelMismatch = "chart.level-mismatch";

    /// <summary>La chart pointe sur un audio different de celui du manifeste.</summary>
    public const string ChartAudioMismatch = "chart.audio-mismatch";

    /// <summary>Les notes ne sont pas triees par temps croissant.</summary>
    public const string NotesUnsorted = "chart.notes-unsorted";

    /// <summary>Une note est hors bornes : temps, duree, force, fenetre ou phase.</summary>
    public const string NoteOutOfRange = "chart.note-out-of-range";

    /// <summary>Le decalage de phase n'est pas un multiple d'un huitieme de temps.</summary>
    public const string NoteBadPhase = "chart.note-bad-phase";

    /// <summary>Le chemin de touche n'est pas dans la grille.</summary>
    public const string NoteBadKey = "chart.note-bad-key";

    /// <summary>La touche n'existe pas.</summary>
    public const string ChartMissingFromPack = "chart.missing-from-pack";

    /// <summary>La duree du niveau annoncee ne couvre pas la derniere note.</summary>
    public const string ChartTooShort = "chart.too-short";
}
