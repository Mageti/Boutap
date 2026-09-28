// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Boutap.Core.Audit;

/// <summary>
/// Codes de constat de l'audit du depot, distincts de ceux du validateur de
/// pack (<see cref="Pack.ValidationCodes"/>).
/// </summary>
/// <remarks>
/// Un code est une promesse de stabilite : un message peut etre reecrit, un
/// code ne doit pas l'etre sans raison majeure. Les codes d'audit sont
/// prefixes par le sujet audite, pour qu'un message de CI dise tout de suite
/// de quoi il parle.
/// </remarks>
public static class AuditCodes
{
    // ------------------------------------------------------------- profils

    /// <summary>Le fichier de profil n'a pas pu etre lu.</summary>
    public const string ProfileUnreadable = "profile.unreadable";

    /// <summary>Le fichier de profil n'est pas du JSON valide.</summary>
    public const string ProfileNotJson = "profile.not-json";

    /// <summary>Un champ obligatoire du profil est absent.</summary>
    public const string ProfileMissingField = "profile.missing-field";

    /// <summary>Un champ du profil a une valeur hors contrat.</summary>
    public const string ProfileBadValue = "profile.bad-value";

    /// <summary>Le champ <c>name</c> ne correspond pas au nom du fichier.</summary>
    public const string ProfileNameMismatch = "profile.name-mismatch";

    /// <summary>Une touche de la grille 3x3 n'est pas declaree.</summary>
    public const string ProfileMissingButton = "profile.missing-button";

    /// <summary>Le profil declare une touche que le format ne connait pas.</summary>
    public const string ProfileUnknownButton = "profile.unknown-button";

    /// <summary>Les deux declarations d'un volant ne concordent pas.</summary>
    public const string ProfileWheelMismatch = "profile.wheel-mismatch";

    // ------------------------------------------------------------- schemas

    /// <summary>Le fichier de schema n'a pas pu etre lu.</summary>
    public const string SchemaUnreadable = "schema.unreadable";

    /// <summary>Le fichier de schema n'est pas du JSON valide.</summary>
    public const string SchemaNotJson = "schema.not-json";

    /// <summary>
    /// Le schema et le code ne decrivent pas le meme format : champ present
    /// dans l'un et absent de l'autre, ou identifiant de schema different.
    /// </summary>
    public const string SchemaContractDrift = "schema.contract-drift";

    // --------------------------------------------------------------- reperes

    /// <summary>Le repertoire audite n'existe pas.</summary>
    /// <summary>L'index d'attente d'un repertoire de packs est illisible.</summary>
    public const string ExpectationIndexUnreadable = "audit.expectation-index-unreadable";

    /// <summary>L'index d'attente n'est pas le JSON attendu.</summary>
    public const string ExpectationIndexNotJson = "audit.expectation-index-not-json";

    /// <summary>L'index d'attente annonce une version que l'outil ne comprend pas.</summary>
    public const string ExpectationIndexVersion = "audit.expectation-index-version";

    /// <summary>Un pack n'est pas conforme a ce que son index annonce.</summary>
    public const string ExpectationMismatch = "audit.expectation-mismatch";

    /// <summary>L'index d'attente annonce un pack qui n'existe pas.</summary>
    public const string ExpectationFileMissing = "audit.expectation-file-missing";

    /// <summary>Un pack d'un repertoire indexe n'est annonce par aucun index.</summary>
    public const string ExpectationMissing = "audit.expectation-missing";

    public const string AuditPathMissing = "audit.path-missing";

    /// <summary>Le chemin audite n'est pas un repertoire.</summary>
    public const string AuditNotADirectory = "audit.not-a-directory";
}
