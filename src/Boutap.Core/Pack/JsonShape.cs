// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json;

namespace Boutap.Core.Pack;

/// <summary>
/// La forme d'un document JSON tel que le format l'interdit, et que le modele
/// ne peut plus dire une fois deserialise.
/// </summary>
/// <remarks>
/// <c>additionalProperties: false</c> existe pour une raison precise : un champ
/// que personne ne lit est une promesse que le lecteur ne tient pas. Il
/// disparait des que le JSON est transforme en objet C#, d'ou cette verification
/// sur le texte d'origine.
/// </remarks>
public static class JsonShape
{
    /// <summary>Signale toute propriete absente de la liste des proprietes connues.</summary>
    /// <param name="json">Le document.</param>
    /// <param name="known">Noms de proprietes attendus a cet niveau.</param>
    /// <param name="jsonPointer">Pointeur JSON du niveau inspecte.</param>
    /// <param name="result">Resultat en cours.</param>
    /// <param name="code">Code d'erreur a employer.</param>
    public static void CheckKnownProperties(
        JsonElement json,
        IReadOnlySet<string> known,
        string jsonPointer,
        ValidationResult result,
        string code)
    {
        if (json.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (JsonProperty property in json.EnumerateObject())
        {
            if (!known.Contains(property.Name))
            {
                result.Error(
                    code,
                    $"« {property.Name} » n'existe pas dans le format. "
                    + $"Champs acceptes : {string.Join(", ", known.Order())}.",
                    jsonPointer.Length == 0 ? property.Name : jsonPointer + "/" + property.Name);
            }
        }
    }

    /// <summary>Signale toute propriete inconnue d'un objet d'un objet.</summary>
    /// <param name="parent">L'objet.</param>
    /// <param name="field">Nom du champ dont la valeur est un objet.</param>
    /// <param name="known">Noms de proprietes attendus.</param>
    /// <param name="code">Code d'erreur a employer.</param>
    /// <param name="result">Resultat en cours.</param>
    public static void CheckFieldsOf(
        JsonElement parent,
        string field,
        IReadOnlySet<string> known,
        string code,
        ValidationResult result)
    {
        if (parent.ValueKind != JsonValueKind.Object
            || !parent.TryGetProperty(field, out JsonElement child)
            || child.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        CheckKnownProperties(child, known, "/" + field, result, code);
    }

    /// <summary>
    /// Parse un document en ayant toujours un element a examiner, meme si le
    /// texte est invalide : la validation doit pouvoir signaler « illisible »
    /// sans que l'appelant ait a le deviner.
    /// </summary>
    /// <param name="json">Texte a parser.</param>
    /// <param name="root">Element obtenu, ou null si le texte est invalide.</param>
    /// <returns>Vrai si le texte est du JSON.</returns>
    public static bool TryParse(string json, out JsonElement? root)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(
                json, new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                });
            root = document.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            root = null;
            return false;
        }
    }
}
