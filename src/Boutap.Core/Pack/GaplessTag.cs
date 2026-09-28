// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Boutap.Core.Pack;

/// <summary>
/// Ce que l'audio embarque comme balise de lecture sans coupure.
/// </summary>
/// <remarks>
/// Les noms ne sont pas ceux de l'enum : le format ecrit <c>itun-smpb</c>, la
/// casse de .NET donnerait <c>ItunSmpb</c>. Le format fait foi, donc la
/// conversion est ecrite a la main.
/// </remarks>
public enum GaplessTag
{
    /// <summary>Balise <c>Xing</c> (LAME).</summary>
    Xing = 0,

    /// <summary>Balise <c>iTunSMPB</c> d'iTunes.</summary>
    ITunSmpb = 1,

    /// <summary>Balise <c>VBRI</c> de Fraunhofer.</summary>
    Vbri = 2,

    /// <summary>Une balise est presente mais inconnue.</summary>
    Unknown = 3,

    /// <summary>Aucune balise.</summary>
    None = 4,
}

/// <summary>Conversions de <see cref="GaplessTag"/> pour System.Text.Json.</summary>
public sealed class GaplessTagJsonConverter : JsonConverter<GaplessTag?>
{
    /// <inheritdoc/>
    public override GaplessTag? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("gapless_metadata s'ecrit entre guillemets.");
        }

        string? text = reader.GetString();
        return text switch
        {
            "xing" => GaplessTag.Xing,
            "itun-smpb" => GaplessTag.ITunSmpb,
            "vbri" => GaplessTag.Vbri,
            "unknown" => GaplessTag.Unknown,
            "none" => GaplessTag.None,
            _ => throw new JsonException(
                $"« {text} » n'est pas une balise de lecture sans coupure : "
                + "attendu « xing », « itun-smpb », « vbri », « unknown » ou « none »."),
        };
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, GaplessTag? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStringValue(value.Value switch
        {
            GaplessTag.Xing => "xing",
            GaplessTag.ITunSmpb => "itun-smpb",
            GaplessTag.Vbri => "vbri",
            GaplessTag.Unknown => "unknown",
            GaplessTag.None => "none",
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Balise inconnue."),
        });
    }
}
