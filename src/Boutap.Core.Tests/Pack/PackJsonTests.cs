// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json;
using Boutap.Core.Common;
using Boutap.Core.Pack;
using Xunit;

namespace Boutap.Core.Tests.Pack;

/// <summary>Lecture et ecriture des documents du format.</summary>
public sealed class PackJsonTests
{
    /// <summary>Ordre d'ecriture attendu du manifeste.</summary>
    private static readonly string[] ManifestOrder =
    [
        "schema", "id", "title", "artist", "audio", "analysis",
        "generator", "charts", "content_license", "min_player_version",
    ];

    /// <summary>Ordre d'ecriture attendu d'une note.</summary>
    private static readonly string[] NoteOrder = ["t", "k", "d", "v", "w", "s"];

    /// <summary>Champs calcules qui ne doivent jamais atteindre le disque.</summary>
    private static readonly string[] CalculatedNoteFields =
        ["ForceOrDefault", "IsHold", "IsWheel", "EndTime", "WindowScaleOrDefault"];

    [Fact]
    public void Le_Manifeste_Ecrit_Les_Champs_Dans_L_Ordre_Du_Schema()
    {
        string json = PackJson.Write(ReadDemoManifest());

        Assert.Equal(ManifestOrder, PropertyNames(json));
    }

    [Fact]
    public void La_Note_Ecrit_Ses_Champs_Dans_L_Ordre_Du_Schema()
    {
        string json = PackJson.Write(new Note
        {
            Time = 1.5d,
            Key = KeyBinding.Grid(0),
            Duration = 0.5d,
            Force = 6,
            WindowScale = 1.25d,
            PhaseShift = 0.125d,
        });

        Assert.Equal(NoteOrder, PropertyNames(json));
        Assert.Equal("{\"t\":1.5,\"k\":0,\"d\":0.5,\"v\":6,\"w\":1.25,\"s\":0.125}", Compact(json));
    }

    [Fact]
    public void Un_Commentaire_Est_Accepte()
    {
        // Le format autorise les commentaires : un pack reste lisible a la main.
        Manifest manifest = PackJson.Read<Manifest>(
            """
            {
              // identifiant du format
              "schema": "boutap/pack-manifest/1",
              "id": "essai",
            }
            """,
            "manifeste");

        Assert.Equal("essai", manifest.Id);
    }

    [Fact]
    public void Une_Virgule_Finale_Est_Acceptee()
    {
        Manifest manifest = PackJson.Read<Manifest>(
            """{"schema":"boutap/pack-manifest/1","id":"essai",}""",
            "manifeste");

        Assert.Equal("essai", manifest.Id);
    }

    [Fact]
    public void Une_Casse_Differente_Est_Refusee()
    {
        // « Audio » n'est pas « audio » : accepter les deux masquerait les
        // fautes de frappe et les fautes de script.
        Manifest manifest = PackJson.Read<Manifest>(
            """{"schema":"boutap/pack-manifest/1","Audio":null}""",
            "manifeste");

        Assert.Null(manifest.Audio);
    }

    [Fact]
    public void Les_Accents_Restent_UTF8()
    {
        string json = PackJson.Write(new Manifest { Title = "Berceuse à la lune — n° 1" });

        Assert.Contains("Berceuse à la lune — n° 1", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Les_Valeurs_Du_Format_Sont_Ecrites_Sous_Leur_Forme()
    {
        string json = Compact(PackJson.Write(new Manifest
        {
            MinPlayerVersion = SemanticVersion.Parse("0.1.0"),
            Audio = new AudioRef { GaplessMetadata = GaplessTag.ITunSmpb },
            Charts = [new ChartEntry { Level = ChartLevel.Ronde }],
        }));

        Assert.Contains("\"min_player_version\":\"0.1.0\"", json, StringComparison.Ordinal);
        Assert.Contains("\"gapless_metadata\":\"itun-smpb\"", json, StringComparison.Ordinal);
        Assert.Contains("\"level\":\"ronde\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Un_Volant_S_Ecrit_Sous_Son_Nom()
    {
        string json = Compact(PackJson.Write(new Note { Time = 0d, Key = KeyBinding.WheelLeft }));

        Assert.Contains("\"k\":\"WHEEL_L\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Aller_Retour_Est_Stable()
    {
        // Reprendre un document, le reecrire, le relire et le reecrire doit
        // donner deux fois le meme octet : c'est ce qui permet de comparer un
        // pack genere ici a un pack genere ailleurs.
        string first = PackJson.Write(ReadDemoManifest());
        string second = PackJson.Write(PackJson.Read<Manifest>(first, "manifeste"));

        Assert.Equal(first, second);
    }

    [Fact]
    public void Un_Document_Invalide_Leve_Une_Erreur_Explicite()
    {
        PackFormatException error = Assert.Throws<PackFormatException>(
            () => PackJson.Read<Manifest>("""{"schema":""", "manifeste du pack de demonstration"));

        Assert.Contains("manifeste du pack de demonstration", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Un_Null_Leve_Une_Erreur_Explicite()
    {
        PackFormatException error = Assert.Throws<PackFormatException>(
            () => PackJson.Read<Manifest>("null", "manifeste"));

        Assert.Contains("null", error.Message, StringComparison.Ordinal);
    }

    private static Manifest ReadDemoManifest()
    {
        using LoadedPack pack = PackReader.Read(TestPaths.DemoPackPath);
        return pack.Manifest;
    }

    private static string[] PropertyNames(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return [.. document.RootElement.EnumerateObject().Select(property => property.Name)];
    }

    private static string Compact(string json) =>
        string.Concat(json.Where(character => !char.IsWhiteSpace(character)));
}
