// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json;
using System.Text.Json.Serialization;
using Boutap.Core.Common;

namespace Boutap.Core.Pack;

/// <summary>
/// La reference au generateur, telle que la chart la porte : nom, version et
/// graine. Pas de <c>params</c> ici : la trace complete vit dans le manifeste,
/// et une chart qui embarque les parametres ferait cambios la taille de chaque
/// fichier pour une information que le manifeste contient deja.
/// </summary>
public sealed record GeneratorRef
{
    /// <summary>Nom du generateur, tel qu'il apparait dans le credit.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>Version du generateur.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("version")]
    public string? Version { get; init; }

    /// <summary>Graine, derivee du contenu decode de l'audio et du niveau.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("seed")]
    public ulong? Seed { get; init; }
}

/// <summary>Une chart : un niveau, pour un audio donne.</summary>
public sealed record Chart
{
    /// <summary>Valeur constante : <c>boutap/chart/1</c>.</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("schema")]
    public string? Schema { get; init; }

    /// <summary>Niveau de ce fichier.</summary>
    [JsonPropertyOrder(4)]
    [JsonPropertyName("level")]
    public ChartLevel? Level { get; init; }

    /// <summary>SHA-256 de l'audio decode, identique a celui du manifeste.</summary>
    [JsonPropertyOrder(5)]
    [JsonPropertyName("audio_sha256")]
    public string? AudioSha256 { get; init; }

    /// <summary>Decalage entre l'audio et la premiere note, en secondes.</summary>
    [JsonPropertyOrder(6)]
    [JsonPropertyName("offset_seconds")]
    public double? OffsetSeconds { get; init; }

    /// <summary>Generateur et graine ayant produit cette chart.</summary>
    [JsonPropertyOrder(7)]
    [JsonPropertyName("generator")]
    public GeneratorRef? Generator { get; init; }

    /// <summary>Notes, triees par <see cref="Note.Time"/> croissant.</summary>
    [JsonPropertyOrder(8)]
    [JsonPropertyName("notes")]
    public IReadOnlyList<Note>? Notes { get; init; }

    /// <summary>Note la plus tardive du niveau, en secondes.</summary>
    [JsonIgnore]
    public double LastNoteTime
    {
        get
        {
            double last = 0;
            if (Notes is not null)
            {
                foreach (Note note in Notes)
                {
                    if (note.Time > last)
                    {
                        last = note.Time;
                    }
                }
            }

            return last;
        }
    }

    /// <summary>Nombre de notes.</summary>
    [JsonIgnore]
    public int NoteCount => Notes?.Count ?? 0;
}

/// <summary>Conversions de <see cref="ChartLevel"/> pour System.Text.Json.</summary>
public sealed class ChartLevelJsonConverter : JsonConverter<ChartLevel?>
{
    /// <inheritdoc/>
    public override ChartLevel? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("Un niveau s'ecrit entre guillemets : « berceau », « ronde » ou « cascade ».");
        }

        string? name = reader.GetString();
        if (!ChartLevels.TryParse(name, out ChartLevel level))
        {
            throw new JsonException($"« {name} » n'est pas un niveau : attendu « berceau », « ronde » ou « cascade ».");
        }

        return level;
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, ChartLevel? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStringValue(value.Value.FileNameOf());
    }
}
