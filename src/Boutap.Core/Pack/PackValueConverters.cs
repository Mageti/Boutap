// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json;
using System.Text.Json.Serialization;
using Boutap.Core.Common;

namespace Boutap.Core.Pack;

/// <summary>Ecrit et lit une version semver sous forme de chaine.</summary>
public sealed class SemanticVersionJsonConverter : JsonConverter<SemanticVersion?>
{
    /// <inheritdoc/>
    public override SemanticVersion? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("Une version s'ecrit entre guillemets, par exemple « 0.1.0 ».");
        }

        string? text = reader.GetString();
        if (!SemanticVersion.TryParse(text, out SemanticVersion? version, out string? error))
        {
            throw new JsonException($"Version invalide : {error}");
        }

        return version;
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, SemanticVersion? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStringValue(value.ToString());
    }
}

/// <summary>Ecrit et lit une tonalite sous forme de chaine, telle qu'ecrite.</summary>
public sealed class KeySignatureJsonConverter : JsonConverter<KeySignature?>
{
    /// <inheritdoc/>
    public override KeySignature? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("Une tonalite s'ecrit entre guillemets, par exemple « F#m ».");
        }

        string? text = reader.GetString();
        if (!KeySignature.TryParse(text, out KeySignature? key))
        {
            throw new JsonException(
                $"« {text} » n'est pas une tonalite : attendu une lettre, un eventuel alteration, "
                + "puis un mode parmi « maj », « min », « m », « dim » et « aug ».");
        }

        return key;
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, KeySignature? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStringValue(value.ToString());
    }
}
