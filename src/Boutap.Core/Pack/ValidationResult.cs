// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Globalization;
using System.Text;

namespace Boutap.Core.Pack;

/// <summary>Gravite d'un constat de validation.</summary>
public enum IssueSeverity
{
    /// <summary>
    /// Le pack est valide, mais quelque chose mérite d'etre dit : une
    /// convention non respectee, une licence improbable.
    /// </summary>
    Warning = 0,

    /// <summary>Le pack n'est pas conforme au format.</summary>
    Error = 1,
}

/// <summary>
/// Un constat, avec un code stable et un pointeur JSON (RFC 6901) vers le
/// champ fautif.
/// </summary>
/// <param name="Severity">Gravite.</param>
/// <param name="Code">Code stable, d'un point, en minuscules.</param>
/// <param name="Message">Ce qui ne va pas, en une phrase.</param>
/// <param name="JsonPointer">
/// Pointeur JSON RFC 6901 vers le champ. La racine du document a pour pointeur
/// la chaine vide. Les deux points d'un echappement : <c>~0</c> pour <c>~</c>,
/// <c>~1</c> pour <c>/</c>.
/// </param>
public sealed record ValidationIssue(
    IssueSeverity Severity,
    string Code,
    string Message,
    string? JsonPointer = null)
{
    /// <summary>Vrai si le constat est une erreur.</summary>
    public bool IsError => Severity == IssueSeverity.Error;

    /// <summary>Point d'entree unique, lisible comme une ligne de terminal.</summary>
    public string Format() => this switch
    {
        { JsonPointer: null or "" } => $"{(IsError ? "erreur" : "avertissement")} {Code} : {Message}",
        _ => $"{(IsError ? "erreur" : "avertissement")} {Code} : {Message} (en {JsonPointer})",
    };
}

/// <summary>
/// L'ensemble des constats sur un pack. On accumule plutot que de lever a la
/// premiere erreur : corriger un pack exige de les voir tous d'un coup, et un
/// validateur qui s'arrete au premier probleme oblige a dix executions.
/// </summary>
public sealed class ValidationResult
{
    private readonly List<ValidationIssue> _issues = new();

    /// <summary>Constats, dans l'ordre de decouverte.</summary>
    public IReadOnlyList<ValidationIssue> Issues => _issues;

    /// <summary>Nombre d'erreurs.</summary>
    public int ErrorCount
    {
        get
        {
            int count = 0;
            foreach (ValidationIssue issue in _issues)
            {
                if (issue.IsError)
                {
                    count++;
                }
            }

            return count;
        }
    }

    /// <summary>Nombre d'avertissements.</summary>
    public int WarningCount => _issues.Count - ErrorCount;

    /// <summary>Vrai si aucune erreur n'a ete trouvee.</summary>
    public bool IsValid => ErrorCount == 0;

    /// <summary>Ajoute un constat.</summary>
    public ValidationResult Add(IssueSeverity severity, string code, string message, string? jsonPointer = null)
    {
        _issues.Add(new ValidationIssue(severity, code, message, jsonPointer));
        return this;
    }

    /// <summary>Ajoute une erreur.</summary>
    public ValidationResult Error(string code, string message, string? jsonPointer = null) =>
        Add(IssueSeverity.Error, code, message, jsonPointer);

    /// <summary>Ajoute un avertissement.</summary>
    public ValidationResult Warning(string code, string message, string? jsonPointer = null) =>
        Add(IssueSeverity.Warning, code, message, jsonPointer);

    /// <summary>Ajoute une erreur si la condition est fausse, et renvoie la condition.</summary>
    public bool Require(
        bool condition,
        string code,
        string message,
        string? jsonPointer = null)
    {
        if (!condition)
        {
            Error(code, message, jsonPointer);
        }

        return condition;
    }

    /// <summary>Ajoute les constats d'un autre resultat.</summary>
    public ValidationResult Merge(ValidationResult other)
    {
        _issues.AddRange(other._issues);
        return this;
    }

    /// <summary>Prolonge une collection pour un index, comme <c>/notes/17</c>.</summary>
    public static string JsonPointerAt(string parent, int index) => $"{parent}/{index.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Prolonge une collection pour un nom de champ, comme <c>/notes/17/v</c>.</summary>
    public static string JsonPointerField(string parent, string field) => $"{parent}/{field}";

    /// <summary>Le rapport, en texte.</summary>
    public string Report()
    {
        StringBuilder builder = new();
        foreach (ValidationIssue issue in _issues)
        {
            builder.Append(issue.Format()).Append('\n');
        }

        return builder.ToString();
    }

    /// <summary>Le rapport, en JSON, pour une utilisation machine.</summary>
    public string ToJson() => PackJson.Write(new
    {
        valid = IsValid,
        errors = ErrorCount,
        warnings = WarningCount,
        issues = _issues.Select(i => new
        {
            severity = i.IsError ? "error" : "warning",
            code = i.Code,
            message = i.Message,
            jsonPointer = i.JsonPointer ?? string.Empty,
        }),
    });
}
