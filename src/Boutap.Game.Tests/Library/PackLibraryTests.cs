// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.IO.Compression;
using System.Text;

using Boutap.Core.Pack;
using Boutap.Game.Library;
using Xunit;

namespace Boutap.Game.Tests.Library;

public sealed class PackLibraryTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "boutap-library-" + Path.GetRandomFileName());

    public PackLibraryTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Un fichier encore ouvert par Windows ne doit pas faire échouer les tests.
        }
    }

    [Fact]
    public void UnDossierVideNeContientAucunMorceau()
    {
        Assert.Empty(PackLibrary.Scan(_directory));
    }

    [Fact]
    public void UnDossierAbsentNeCassePasLePremierLancement()
    {
        Assert.Empty(PackLibrary.Scan(Path.Combine(_directory, "ailleurs")));
    }

    [Fact]
    public void LesMorceauxSontTriesParTitre()
    {
        WritePack("zebre.btp", "zebre", "Zèbre");
        WritePack("alpha.btp", "alpha", "Alpha");
        WritePack("milieu.btp", "milieu", "Milieu");

        IReadOnlyList<LibraryEntry> found = PackLibrary.Scan(_directory);

        Assert.Equal(["Alpha", "Milieu", "Zèbre"], found.Select(e => e.DisplayTitle).ToArray());
    }

    [Fact]
    public void UnPackIllisibleEstListeAvecSaRaison()
    {
        File.WriteAllText(Path.Combine(_directory, "cassé.btp"), "ceci n'est pas une archive");

        IReadOnlyList<LibraryEntry> found = PackLibrary.Scan(_directory);

        LibraryEntry entry = Assert.Single(found);
        Assert.False(entry.IsPlayable);
        Assert.NotNull(entry.Error);
    }

    [Fact]
    public void UnPackIllisibleNEmpechePasLesAutresDeSAfficher()
    {
        WritePack("bon.btp", "bon", "Bon morceau");
        File.WriteAllText(Path.Combine(_directory, "mauvais.btp"), "rien du tout");

        IReadOnlyList<LibraryEntry> found = PackLibrary.Scan(_directory);

        Assert.Equal(2, found.Count);
        Assert.Single(found, e => e.IsPlayable);
    }

    [Fact]
    public void LesNiveauxSontTriesDuPlusSimpleAuPlusDense()
    {
        WritePack("desordre.btp", "desordre", "Désordre", levels: new[]
        {
            (ChartLevel.Cascade, "charts/cascade.json"),
            (ChartLevel.Berceau, "charts/berceau.json"),
            (ChartLevel.Ronde, "charts/ronde.json"),
        });

        LibraryEntry entry = Assert.Single(PackLibrary.Scan(_directory));

        Assert.Equal(
            [ChartLevel.Berceau, ChartLevel.Ronde, ChartLevel.Cascade],
            entry.Levels.Select(l => l.Level).ToArray());
    }

    [Fact]
    public void LEentreePorteLeTitreLeTempoLaTonaliteEtLaDuree()
    {
        WritePack("complet.btp", "complet", "Morceau complet");

        LibraryEntry entry = Assert.Single(PackLibrary.Scan(_directory));

        Assert.Equal("Morceau complet", entry.DisplayTitle);
        Assert.Equal("Boutap contributors", entry.Artist);
        Assert.Equal(20, entry.DurationSeconds);
        Assert.Equal(137.3, entry.Bpm);
        Assert.NotNull(entry.Key);
        Assert.Equal("A", entry.Key!.TonicName);
        Assert.Equal(3, entry.Levels.Count);
        Assert.Equal(40, entry.Levels[0].NoteCount);
        Assert.Equal(7, entry.Levels[0].Rating);
        Assert.Equal(2.5, entry.Levels[0].Nps);
    }

    [Fact]
    public void UneDureeSeLitEnMinutesEtSecondes()
    {
        Assert.Equal("1:05", LibraryEntry.FormatDuration(65));
        Assert.Equal("3:00", LibraryEntry.FormatDuration(180));
        Assert.Equal("0:00", LibraryEntry.FormatDuration(0.1));
        Assert.Equal("4:00", LibraryEntry.FormatDuration(240));
    }

    [Fact]
    public void UnPackSansNiveauNestPasJouable()
    {
        WritePack("vide.btp", "vide", "Sans niveaux", noCharts: true);

        LibraryEntry entry = Assert.Single(PackLibrary.Scan(_directory));

        Assert.Empty(entry.Levels);
        Assert.False(entry.IsPlayable);
    }

    [Fact]
    public void LExtensionEstInsensibleALaCasse()
    {
        WritePack("MAJUSCULE.BTP", "majuscule", "En majuscules");

        IReadOnlyList<LibraryEntry> found = PackLibrary.Scan(_directory);

        Assert.Single(found);
    }

    [Fact]
    public void UnFichierQuiNestPasUnPackEstIgnore()
    {
        File.WriteAllText(Path.Combine(_directory, "notes.txt"), "pas un pack");
        File.WriteAllText(Path.Combine(_directory, "pochette.png"), "pas un pack non plus");

        Assert.Empty(PackLibrary.Scan(_directory));
    }

    [Fact]
    public void LeTitreManquantPrendLeNomDuFichier()
    {
        WritePack("sans-titre.btp", "sans-titre", title: null);

        LibraryEntry entry = Assert.Single(PackLibrary.Scan(_directory));

        Assert.Equal("sans-titre.btp", entry.DisplayTitle);
    }

    /// <summary>Écrit un pack valide dans le dossier des tests.</summary>
    private string WritePack(
        string fileName,
        string id,
        string? title = null,
        bool noCharts = false,
        params (ChartLevel Level, string File)[] levels)
    {
        if (levels.Length == 0)
        {
            levels =
            [
                (ChartLevel.Berceau, "charts/berceau.json"),
                (ChartLevel.Ronde, "charts/ronde.json"),
                (ChartLevel.Cascade, "charts/cascade.json"),
            ];
        }
        var path = Path.Combine(_directory, fileName);
        var charts = noCharts ? "[]" : string.Join(",", levels.Select(static lvl =>
            $$"""
              {"level":"{{lvl.Level.FileNameOf()}}","file":"{{lvl.File}}","note_count":40,"duration_seconds":20.0,"nps":2.5,"peak_load":0.25,"rating":7}
            """));
        var titleJson = title is null
            ? "\"title\":null"
            : $"\"title\":{System.Text.Json.JsonSerializer.Serialize(title)}";

        var manifest = $$"""
            {
              "schema":"boutap/pack-manifest/1",
              "id":"{{id}}",
              {{titleJson}},
              "artist":"Boutap contributors",
              "audio":{"path":"audio.wav","sha256":"a4f7183192ae47b109a070701f5e6015d71f3b3a92ace4b906aeb846e16b4ec9","duration_seconds":20.0,"pre_skip_seconds":0.0,"gapless_metadata":"none"},
              "analysis":{"duration_seconds":20.0,"bpm":137.3,"bpm_confidence":0.4,"key":"Am","key_confidence":0.75},
              "generator":{"name":"boutap-gen","version":"0.1.0","params":{},"seed":1},
              "charts":[{{charts}}],
              "content_license":"CC0-1.0",
              "min_player_version":"0.1.0"
            }
            """;

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        using (var writer = new StreamWriter(archive.CreateEntry("manifest.json").Open(), Encoding.UTF8))
        {
            writer.Write(manifest);
        }

        using (var entry = archive.CreateEntry("audio.wav").Open())
        {
            // 44 octets d'en-tête WAV valide, sans données : la bibliothèque ne
            // lit jamais l'audio, mais un pack plausible a un audio plausible.
            byte[] silence = new byte[44];
            entry.Write(silence);
        }

        if (levels.Length > 0)
        {
            using (var first = archive.CreateEntry(levels[0].File).Open())
            {
                first.Write("{\"t\":0}"u8.ToArray());
            }
        }

        return path;
    }
}
