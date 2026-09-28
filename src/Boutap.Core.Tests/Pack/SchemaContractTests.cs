// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json;
using System.Text.Json.Nodes;
using Boutap.Core.Audit;
using Boutap.Core.Pack;
using Xunit;

namespace Boutap.Core.Tests.Pack;

/// <summary>
/// Le code et le schema doivent decrire le meme format. Ce test est le seul
/// qui relie les deux : sans lui, renommer un champ casse la lecture des packs
/// existants sans qu'aucun test ne le voie.
/// </summary>
public sealed class SchemaContractTests
{
    [Fact]
    public void Schemas_Commis_Decrivent_Le_Modele()
    {
        ValidationResult result = new();

        SchemaContract.CheckDirectory(TestPaths.SchemaDirectory, result);

        Assert.True(
            result.IsValid,
            "Les schemas commits ne decrivent plus le modele :" + Environment.NewLine + result.Report());
        Assert.Equal(0, result.WarningCount);
    }

    [Fact]
    public void Manifest_Est_Ecrit_Sous_Les_Noms_Du_Schema()
    {
        AssertSameFields(SchemaContract.ManifestFields(), SchemaContract.SerializedManifestFields());
    }

    [Fact]
    public void Chart_Est_Ecrite_Sous_Les_Noms_Du_Schema()
    {
        AssertSameFields(SchemaContract.ChartFields(), SchemaContract.SerializedChartFields());
    }

    [Fact]
    public void Note_Est_Ecrite_Sous_Les_Noms_Courts_Du_Format()
    {
        AssertSameFields(SchemaContract.NoteFields(), SchemaContract.SerializedNoteFields());
    }

    [Fact]
    public void Une_Propriete_Calculee_N_Est_Jamais_Ecrite()
    {
        // Les proprietes derivees (ForceOrDefault, IsHold, EndTime...) sont
        // utiles au code et interdites sur le disque : un lecteur tiers doit
        // refuser un champ qu'il ne connait pas, donc le pack ne peut pas en
        // contenir un qui sort du modele.
        string json = PackJson.Write(new Note
        {
            Time = 1.5d,
            Key = KeyBinding.Grid(4),
            Duration = 0.5d,
        });

        foreach (string name in new[] { "ForceOrDefault", "IsHold", "IsWheel", "EndTime", "WindowScaleOrDefault" })
        {
            Assert.DoesNotContain("\"" + name + "\"", json, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Le_Controle_De_Noms_Voit_Un_Champ_A_Jour_Que_Le_Modele_Ignore()
    {
        ValidationResult result = new();

        SchemaContract.CheckSerializedNames(
            "chart.schema.json",
            SchemaContract.ChartFields(),
            new HashSet<string>(SchemaContract.ChartFields(), StringComparer.Ordinal) { "niveau" },
            result);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Message.Contains("niveau", StringComparison.Ordinal));
    }

    [Fact]
    public void Le_Controle_De_Noms_Voit_Un_Champ_Oublie_Par_Le_Modele()
    {
        ValidationResult result = new();

        SchemaContract.CheckSerializedNames(
            "chart.schema.json",
            new HashSet<string>(SchemaContract.ChartFields(), StringComparer.Ordinal) { "charts" },
            SchemaContract.ChartFields(),
            result);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Message.Contains("charts", StringComparison.Ordinal));
    }

    [Fact]
    public void Une_Derive_Du_Schema_Est_Signalee()
    {
        string directory = CopySchemas();
        try
        {
            JsonNode chart = ReadNode(Path.Combine(directory, SchemaContract.ChartFileName));
            JsonObject properties = (JsonObject)chart["properties"]!;
            properties["niveau"] = new JsonObject();
            ((JsonObject)properties["schema"]!)["const"] = "boutap/chart/2";
            chart["additionalProperties"] = true;
            File.WriteAllText(Path.Combine(directory, SchemaContract.ChartFileName), chart.ToJsonString());

            ValidationResult result = new();
            SchemaContract.CheckDirectory(directory, result);

            Assert.False(result.IsValid);
            Assert.Equal(2, result.ErrorCount);
            Assert.Equal(1, result.WarningCount);
            Assert.Contains(result.Issues, issue => issue.Message.Contains("boutap/chart/2", StringComparison.Ordinal));
            Assert.Contains(result.Issues, issue => issue.Message.Contains("niveau", StringComparison.Ordinal));
            Assert.Contains(result.Issues, issue => issue.Severity == IssueSeverity.Warning);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Un_Schema_Illisible_Est_Signale()
    {
        string directory = TestPaths.NewTemporaryDirectory();
        try
        {
            File.WriteAllText(Path.Combine(directory, SchemaContract.ChartFileName), "{ pas du json");

            ValidationResult result = new();
            SchemaContract.CheckDirectory(directory, result);

            Assert.False(result.IsValid);
            Assert.Contains(result.Issues, issue => issue.Code == AuditCodes.SchemaNotJson);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Un_Schema_Sans_Proprietes_Est_Signale()
    {
        string directory = TestPaths.NewTemporaryDirectory();
        try
        {
            File.WriteAllText(Path.Combine(directory, SchemaContract.ChartFileName), """{"type":"object"}""");

            ValidationResult result = new();
            SchemaContract.CheckDirectory(directory, result);

            Assert.False(result.IsValid);
            Assert.Contains(result.Issues, issue => issue.Code == AuditCodes.SchemaNotJson);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void AssertSameFields(IReadOnlySet<string> expected, IReadOnlySet<string> serialized)
    {
        Assert.Equal(expected.OrderBy(f => f, StringComparer.Ordinal), serialized.OrderBy(f => f, StringComparer.Ordinal));
    }

    private static string CopySchemas()
    {
        string directory = TestPaths.NewTemporaryDirectory();
        foreach (string name in new[] { SchemaContract.ManifestFileName, SchemaContract.ChartFileName })
        {
            File.Copy(Path.Combine(TestPaths.SchemaDirectory, name), Path.Combine(directory, name));
        }

        return directory;
    }

    private static JsonNode ReadNode(string path) =>
        JsonNode.Parse(File.ReadAllText(path))
        ?? throw new InvalidOperationException(path + " ne contient pas de JSON.");
}
