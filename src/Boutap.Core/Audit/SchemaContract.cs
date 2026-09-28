// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json;
using System.Text.Json.Serialization;
using Boutap.Core.Pack;

namespace Boutap.Core.Audit;

/// <summary>
/// Verifie que les schemas JSON de <c>schema/</c> et le modele C# decrivent le
/// meme format.
/// </summary>
/// <remarks>
/// <para>
/// Un schema JSON n'est pas execute : rien ne garantit qu'un lecteur et un
/// validateur de schema ne dabordent avec l'ecriture. Le modele C#, lui, est
/// compile — et un champ ajoute au modele sans ajouter au schema passe tous
/// les tests, parce que les tests passent par le modele. Cette classe est le
/// maillon manquant : elle compare les deux et signale toute divergence.
/// </para>
/// <para>
/// Elle ne verifie pas la totalite de la grammaire JSON Schema — un
/// validateur tiers le fait mieux. Elle verifie ce qui casse en silence : un
/// nom de champ present d'un seul cote, un identifiant de schema different, un
/// <c>additionalProperties</c> devenu vrai.
/// </para>
/// </remarks>
public static class SchemaContract
{
    /// <summary>Nom de fichier du schema de manifeste.</summary>
    public const string ManifestFileName = "manifest.schema.json";

    /// <summary>Nom de fichier du schema de chart.</summary>
    public const string ChartFileName = "chart.schema.json";

    /// <summary>Compare les deux schemas a la racine du depot.</summary>
    /// <param name="schemaDirectory">Repertoire contenant les deux schemas.</param>
    /// <param name="result">Constats accumules.</param>
    public static void CheckDirectory(string schemaDirectory, ValidationResult result)
    {
        ArgumentNullException.ThrowIfNull(schemaDirectory);
        ArgumentNullException.ThrowIfNull(result);

        string manifestSchema = Path.Combine(schemaDirectory, ManifestFileName);
        string chartSchema = Path.Combine(schemaDirectory, ChartFileName);

        CheckFile(manifestSchema, PackFormat.ManifestSchema, ManifestFields(), result);
        CheckFile(chartSchema, PackFormat.ChartSchema, ChartFields(), result);

        // Le schema doit decrire ce qui sort du serialiseur, pas ce que le
        // modele croit ecrire. Les deux listes sont comparees separement pour
        // que le message dise quel des deux des deux a change.
        CheckSerializedNames(
            ManifestFileName,
            ManifestFields(),
            SerializedManifestFields(),
            result);
        CheckSerializedNames(
            ChartFileName,
            ChartFields(),
            SerializedChartFields(),
            result);
        CheckSerializedNames(
            ChartFileName + " (note)",
            NoteFields(),
            SerializedNoteFields(),
            result);
    }

    /// <summary>Noms de champs d'une note, lus par le modele.</summary>
    public static IReadOnlySet<string> NoteFields() => new HashSet<string>(PackShape.NoteFields, StringComparer.Ordinal);

    /// <summary>Compare un schema et le modele.</summary>
    /// <param name="path">Chemin du schema.</param>
    /// <param name="expectedSchemaValue">Valeur attendue de <c>properties.schema.const</c>.</param>
    /// <param name="modelFields">Noms de champs connus du modele.</param>
    /// <param name="result">Constats accumules.</param>
    public static void CheckFile(
        string path,
        string expectedSchemaValue,
        IReadOnlySet<string> modelFields,
        ValidationResult result)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(modelFields);
        ArgumentNullException.ThrowIfNull(result);

        string label = Path.GetFileName(path);

        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            result.Error(AuditCodes.SchemaUnreadable, $"{label} est illisible : {ex.Message}", label);
            return;
        }

        if (!JsonShape.TryParse(json, out JsonElement? root) || root is null)
        {
            result.Error(AuditCodes.SchemaNotJson, $"{label} n'est pas du JSON valide.", label);
            return;
        }

        JsonElement element = root.Value;
        if (element.ValueKind != JsonValueKind.Object)
        {
            result.Error(AuditCodes.SchemaNotJson, $"{label} n'est pas un objet JSON.", label);
            return;
        }

        // 1. L'identifiant de format : la chaine la plus courte du fichier, et
        //    celle dont une divergence passe le plus inaperceu.
        string? declared = ReadConst(element, "schema");
        if (declared != expectedSchemaValue)
        {
            result.Error(
                AuditCodes.SchemaContractDrift,
                $"{label} annonce « {declared ?? "rien"} » comme identifiant de format ; le code attend « {expectedSchemaValue} ».",
                $"{label}#/properties/schema/const");
        }

        // 2. Le refus des champs inconnus. S'il disparait, un pack peut
        //    contenir n'importe quoi et le validateur de schema acceptera.
        if (element.TryGetProperty("additionalProperties", out JsonElement additional) &&
            additional.ValueKind == JsonValueKind.False)
        {
            // conforme : c'est l'attendu.
        }
        else
        {
            result.Warning(
                AuditCodes.SchemaContractDrift,
                $"{label} n'interdit pas explicitement les champs inconnus ; un Lecteur tiers accepterait n'importe quoi.",
                $"{label}#/additionalProperties");
        }

        // 3. Les noms de champs, dans les deux sens.
        if (!element.TryGetProperty("properties", out JsonElement properties) ||
            properties.ValueKind != JsonValueKind.Object)
        {
            result.Error(AuditCodes.SchemaNotJson, $"{label} n'a pas de membre « properties ».", label);
            return;
        }

        HashSet<string> schemaFields = new(StringComparer.Ordinal);
        foreach (JsonProperty property in properties.EnumerateObject())
        {
            schemaFields.Add(property.Name);
        }

        foreach (string missing in modelFields.Except(schemaFields).OrderBy(f => f, StringComparer.Ordinal))
        {
            result.Error(
                AuditCodes.SchemaContractDrift,
                $"Le modele lit « {missing} », absent de {label}.",
                $"{label}#/properties");
        }

        foreach (string extra in schemaFields.Except(modelFields).OrderBy(f => f, StringComparer.Ordinal))
        {
            result.Error(
                AuditCodes.SchemaContractDrift,
                $"{label} exige « {extra} », que le modele ne lit pas : un pack valide selon le schema serait rejete a la lecture.",
                $"{label}#/properties/{extra}");
        }
    }

    /// <summary>
    /// Noms de champs que le modele ecrit reellement sur le disque.
    /// </summary>
    /// <remarks>
    /// Comparer le schema a une liste de chaines codees en dur ne prouve rien :
    /// il suffit qu'un attribut <c>JsonPropertyName</c> disparaisse pour que la
    /// liste reste verte alors que le serialiseur emet du PascalCase. La seule
    /// comparaison honnete passe par le serialiseur lui-meme : on serialise une
    /// instance temoin et on lit les noms qu'il a produits.
    /// </remarks>
    public static IReadOnlySet<string> SerializedManifestFields() => SerializedFields(new Manifest());

    /// <summary>Noms de champs que le modele ecrit reellement pour une chart.</summary>
    public static IReadOnlySet<string> SerializedChartFields() => SerializedFields(
        new Chart { Notes = [new Note { Time = 0d, Key = KeyBinding.Grid(0) }] });

    /// <summary>
    /// Compare les noms reellement serialises a une liste de champs attendus.
    /// </summary>
    /// <param name="label">Nom du schema, pour le message.</param>
    /// <param name="expected">Noms de champs attendus.</param>
    /// <param name="serialized">Noms reellement emis par le serialiseur.</param>
    /// <param name="result">Constats accumules.</param>
    public static void CheckSerializedNames(
        string label,
        IReadOnlySet<string> expected,
        IReadOnlySet<string> serialized,
        ValidationResult result)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(serialized);
        ArgumentNullException.ThrowIfNull(result);

        foreach (string missing in expected.Except(serialized).OrderBy(f => f, StringComparer.Ordinal))
        {
            result.Error(
                AuditCodes.SchemaContractDrift,
                $"Le modele declare « {missing} », mais le serialiseur ne l'ecrit pas.",
                $"{label}#/properties");
        }

        foreach (string extra in serialized.Except(expected).OrderBy(f => f, StringComparer.Ordinal))
        {
            result.Error(
                AuditCodes.SchemaContractDrift,
                $"Le serialiseur ecrit « {extra} », que le modele ne declare pas : un Lecteur tiers refusera ce champ.",
                $"{label}#/properties/{extra}");
        }
    }

    /// <summary>Noms de champs que le modele ecrit reellement pour une note.</summary>
    public static IReadOnlySet<string> SerializedNoteFields() => SerializedFields(
        new Note { Time = 0d, Key = KeyBinding.Grid(0), Duration = 0.5d, Force = 4 });

    private static HashSet<string> SerializedFields<T>(T sample)
    {
        string json = PackJson.Write(sample);
        HashSet<string> names = new(StringComparer.Ordinal);
        if (JsonShape.TryParse(json, out JsonElement? root) && root is not null
            && root.Value.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in root.Value.EnumerateObject())
            {
                names.Add(property.Name);
            }
        }

        return names;
    }

    /// <summary>Noms de champs du manifeste, lus par le modele.</summary>
    public static IReadOnlySet<string> ManifestFields() => new HashSet<string>(PackShape.ManifestFields, StringComparer.Ordinal);

    /// <summary>Noms de champs de la chart, lus par le modele.</summary>
    public static IReadOnlySet<string> ChartFields() => new HashSet<string>(PackShape.ChartFields, StringComparer.Ordinal);

    private static string? ReadConst(JsonElement element, string field)
    {
        if (!element.TryGetProperty("properties", out JsonElement properties) ||
            properties.ValueKind != JsonValueKind.Object ||
            !properties.TryGetProperty(field, out JsonElement value) ||
            value.ValueKind != JsonValueKind.Object ||
            !value.TryGetProperty("const", out JsonElement constant))
        {
            return null;
        }

        return constant.ValueKind == JsonValueKind.String ? constant.GetString() : null;
    }
}
