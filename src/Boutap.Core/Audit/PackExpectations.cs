// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json;
using Boutap.Core.Pack;

namespace Boutap.Core.Audit;

/// <summary>
/// Ce qu'un <c>index.json</c> attend d'un pack volontairement non conforme.
/// </summary>
/// <remarks>
/// <para>
/// Le depot contient des packs qui doivent <em>echouer</em> : sans eux, on ne
/// teste pas que le validateur refuse quelque chose. Mais un audit qui compte
/// leurs erreurs comme des erreurs de depot n'est plus vert jamais, et l'audit
/// cesse de dire quoi que ce soit.
/// </para>
/// <para>
/// L'index est la sortie : il dit, pour chaque pack casse, combien d'erreurs
/// on attend et avec quels codes. Le validateur n'est pas affaibli, et le test
/// devient plus fort qu'un simple « il y a au moins une erreur » — si le
/// validateur cesse de voir un champ inconnu, l'index le remarque.
/// </para>
/// </remarks>
public sealed record PackExpectation
{
    /// <summary>
    /// Nom du pack, relatif au repertoire qui contient l'index.
    /// </summary>
    /// <remarks>
    /// La propriete ne s'appelle pas <c>File</c> : dans une classe qui lit des
    /// fichiers, ce nom entre en conflit avec <c>System.IO.File</c> et oblige a ecrire
    /// <c>System.IO.File</c> partout. Le champ JSON, lui, reste « file ».
    /// </remarks>
    public string PackFile { get; init; } = string.Empty;

    /// <summary>Nombre exact d'erreurs attendu, si le pack doit etre valide.</summary>
    public int? Errors { get; init; }

    /// <summary>Nombre minimal d'erreurs attendu.</summary>
    public int? MinErrors { get; init; }

    /// <summary>Nombre exact d'avertissements attendu, si declare.</summary>
    public int? Warnings { get; init; }

    /// <summary>Codes de constat qui doivent tous apparaitre.</summary>
    public IReadOnlyList<string> Codes { get; init; } = [];

    /// <summary>Version du format d'index comprise par ce code.</summary>
    public const int SupportedVersion = 1;

    /// <summary>Nom du fichier d'index cherche a cote des packs.</summary>
    public const string IndexFileName = "index.json";

    /// <summary>Lit les attentes d'un repertoire, s'il en porte un.</summary>
    /// <param name="directory">Repertoire contenant des packs.</param>
    /// <param name="map">Les attentes, indexees par nom de fichier.</param>
    /// <param name="result">Constats globaux, en cas d'index illisible.</param>
    /// <returns>Vrai si le repertoire porte un index exploitable.</returns>
    public static bool TryLoad(
        string directory,
        out IReadOnlyDictionary<string, PackExpectation> map,
        ValidationResult result)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(result);

        map = new Dictionary<string, PackExpectation>(StringComparer.Ordinal);

        string path = Path.Combine(directory, IndexFileName);
        if (!File.Exists(path))
        {
            return false;
        }

        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            result.Error(AuditCodes.ExpectationIndexUnreadable, $"{path} est illisible : {ex.Message}", path);
            return false;
        }

        if (!TryParse(json, path, out Dictionary<string, PackExpectation> parsed, result))
        {
            return false;
        }

        map = parsed;
        return true;
    }

    /// <summary>Verifie que le resultat reel correspond a l'attente.</summary>
    /// <param name="label">Nom du pack, pour le message.</param>
    /// <param name="actual">Ce que le validateur a trouve.</param>
    /// <param name="result">Constats a ajouter si l'attente n'est pas tenue.</param>
    public void Check(string label, ValidationResult actual, ValidationResult result)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(actual);
        ArgumentNullException.ThrowIfNull(result);

        if (Errors is int expected && actual.ErrorCount != expected)
        {
            result.Error(
                AuditCodes.ExpectationMismatch,
                $"{label} : {actual.ErrorCount} erreur(s), alors que l'index en attend {expected}. "
                + "Si le validateur a change, c'est l'index qu'il faut mettre a jour — au sens fort.",
                label);
        }

        if (MinErrors is int minimum && actual.ErrorCount < minimum)
        {
            result.Error(
                AuditCodes.ExpectationMismatch,
                $"{label} : {actual.ErrorCount} erreur(s), alors que l'index en attend au moins {minimum}.",
                label);
        }

        if (Warnings is int warnings && actual.WarningCount != warnings)
        {
            result.Error(
                AuditCodes.ExpectationMismatch,
                $"{label} : {actual.WarningCount} avertissement(s), alors que l'index en attend {warnings}.",
                label);
        }

        foreach (string code in Codes)
        {
            if (!actual.Issues.Any(issue => string.Equals(issue.Code, code, StringComparison.Ordinal)))
            {
                result.Error(
                    AuditCodes.ExpectationMismatch,
                    $"{label} : le constat « {code} » attendu par l'index n'a pas ete rendu.",
                    label);
            }
        }
    }

    private static bool TryParse(
        string json,
        string path,
        out Dictionary<string, PackExpectation> map,
        ValidationResult result)
    {
        map = new Dictionary<string, PackExpectation>(StringComparer.Ordinal);

        JsonElement root;
        try
        {
            using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
            root = document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            result.Error(AuditCodes.ExpectationIndexNotJson, $"{path} n'est pas du JSON : {ex.Message}", path);
            return false;
        }

        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("version", out JsonElement version)
            || !version.TryGetInt32(out int value)
            || value != SupportedVersion)
        {
            result.Error(
                AuditCodes.ExpectationIndexVersion,
                $"{path} : version d'index {Describe(root)} inconnue de cette version de l'outil "
                + $"(seule la {SupportedVersion} est comprise).",
                path);
            return false;
        }

        if (!root.TryGetProperty("expectations", out JsonElement expectations)
            || expectations.ValueKind != JsonValueKind.Array)
        {
            result.Error(
                AuditCodes.ExpectationIndexNotJson,
                $"{path} : « expectations » doit etre un tableau.",
                path);
            return false;
        }

        foreach (JsonElement item in expectations.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object
                || !item.TryGetProperty("file", out JsonElement file)
                || file.ValueKind != JsonValueKind.String)
            {
                result.Error(
                    AuditCodes.ExpectationIndexNotJson,
                    $"{path} : chaque attente doit etre un objet avec un champ « file ».",
                    path);
                return false;
            }

            string name = file.GetString() ?? string.Empty;
            PackExpectation expectation = new()
            {
                PackFile = name,
                Errors = ReadInt(item, "errors"),
                MinErrors = ReadInt(item, "min_errors"),
                Warnings = ReadInt(item, "warnings"),
                Codes = ReadStrings(item, "codes"),
            };

            if (!map.TryAdd(name, expectation))
            {
                result.Error(
                    AuditCodes.ExpectationMismatch,
                    $"{path} : « {name} » est annonce deux fois.",
                    path);
                return false;
            }
        }

        foreach (string name in map.Keys)
        {
            if (!File.Exists(Path.Combine(Path.GetDirectoryName(path) ?? ".", name)))
            {
                result.Error(
                    AuditCodes.ExpectationFileMissing,
                    $"{path} annonce « {name} », qui n'existe pas.",
                    path);
                return false;
            }
        }

        return true;
    }

    private static string Describe(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty("version", out JsonElement version)
            ? version.ToString()
            : "absente";

    private static int? ReadInt(JsonElement item, string name) =>
        item.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : null;

    private static List<string> ReadStrings(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        List<string> names = new();
        foreach (JsonElement entry in value.EnumerateArray())
        {
            if (entry.ValueKind == JsonValueKind.String && entry.GetString() is { } text)
            {
                names.Add(text);
            }
        }

        return names;
    }
}
