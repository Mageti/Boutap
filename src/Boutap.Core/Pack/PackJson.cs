// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;

namespace Boutap.Core.Pack;

/// <summary>
/// Comment le format se lit et s'ecrit. Un seul endroit, pour qu'une relecture
/// et une reecriture produisent le meme octet.
/// </summary>
public static class PackJson
{
    /// <summary>Options de lecture : JSONC tolere, noms de champs exacts.</summary>
    public static JsonSerializerOptions ReadOptions { get; } = Build(writeIndented: false);

    /// <summary>Options d'ecriture : indentees, UTF-8 sans echappement unicode.</summary>
    public static JsonSerializerOptions WriteOptions { get; } = Build(writeIndented: true);

    /// <summary>
    /// Options pour comparer deux documents sans se soucier de la mise en
    /// forme. La CI compare ainsi les fixtures : ce qui compte est la
    /// structure, pas l'indentation.
    /// </summary>
    public static JsonSerializerOptions CompareOptions { get; } = Build(writeIndented: false);

    private static JsonSerializerOptions Build(bool writeIndented) => new()
    {
        // Le format autorise les commentaires et les virgules finales : c'est
        // du JSONC, et c'est volontaire, pour qu'un pack reste lisible a la
        // main. Un parseur strict refuserait des packs par ailleurs valides.
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = writeIndented,

        // Les noms de champs du format sont en snake_case et font foi. Une
        // lecture case-insensitive accepterait « Audio_SHA256 », ce qui
        // n'est pas le format et masque les fautes de frappe.
        PropertyNameCaseInsensitive = false,

        // Les accents restent en UTF-8. « é » en « \u00e9 » double la taille
        // d'un titre pour rien, et le fichier reste valide.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,

        // Un nombre JSON non representable en double doit etre refuse, pas
        // arrondi en silence : un `t` de 0,1 deviendrait un temps faux.
        NumberHandling = JsonNumberHandling.Strict,

        // L'ordre des proprietes vient de JsonPropertyOrder sur chaque
        // modele, donc l'ecriture est deterministe.
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,

        // Les convertisseurs sont enregistres ici plutot que portes par un
        // attribut sur chaque type : le format les impose, donc c'est le
        // lecteur du format qui les impose. KeyBinding est la seule exception,
        // porteuse d'un attribut, parce que le type est aussi utilise hors du
        // format, ou « WHEEL_L » n'a pas de sens.
        Converters =
        {
            new GaplessTagJsonConverter(),
            new ChartLevelJsonConverter(),
            new SemanticVersionJsonConverter(),
            new KeySignatureJsonConverter(),
        },
    };

    /// <summary>Deserialise un manifeste, en levant une erreur unique et explicite.</summary>
    public static T Read<T>(string json, string what) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, ReadOptions)
                ?? throw new JsonException($"« {what} » est vide : il contient « null ».");
        }
        catch (JsonException ex)
        {
            throw new PackFormatException($"{what} : {ex.Message}", ex);
        }
    }

    /// <summary>Serialise un modele du format.</summary>
    public static string Write<T>(T value) => JsonSerializer.Serialize(value, WriteOptions);
}
