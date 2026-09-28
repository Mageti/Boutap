// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Boutap.Core.Audit;
using Boutap.Core.Pack;
using Boutap.Core.Tests;
using Xunit;

namespace Boutap.Audio.Tests;

/// <summary>
/// Chaque pack de <c>tests/data/fixtures</c> est valide, volontairement
/// invalide, ou les deux. <c>index.json</c> dit lequel : ce test lit cette
/// declaration plutot que de la redoubler, sinon les deux finissent par
/// diverger et le test vert ne prouve plus rien.
/// </summary>
public sealed class PackFixtureTests
{
    /// <summary>
    /// Niveaux que le pack de demonstration doit contenir, dans l'ordre
    /// alphabétique : c'est le tri qu'applique l'assertion, pas l'ordre du
    /// manifeste, qui n'a rien d'obligatoire.
    /// </summary>
    private static readonly string[] ExpectedLevels = ["berceau", "cascade", "ronde"];

    [Fact]
    public void Les_Fixtures_Ont_Une_Attente_Declaree()
    {
        IReadOnlyList<PackExpectation> expectations = Load();

        Assert.NotEmpty(expectations);
        foreach (PackExpectation expectation in expectations)
        {
            string path = Path.Combine(TestPaths.FixturesDirectory, expectation.PackFile);
            Assert.True(File.Exists(path), "Fixture declaree mais absente : " + expectation.PackFile);
        }

        foreach (string file in Directory.EnumerateFiles(TestPaths.FixturesDirectory, "*.btp"))
        {
            string name = Path.GetFileName(file);
            Assert.Contains(expectations, expectation =>
                string.Equals(expectation.PackFile, name, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Chaque_Fixture_Produit_Les_Codes_Attendus()
    {
        IReadOnlyList<PackExpectation> expectations = Load();

        foreach (PackExpectation expectation in expectations)
        {
            string path = Path.Combine(TestPaths.FixturesDirectory, expectation.PackFile);
            ValidationResult actual;

            using (LoadedPack pack = PackReader.Read(path))
            {
                actual = PackValidator.Validate(pack, new WavAudioProbe());
            }

            ValidationResult diff = new();
            new PackExpectation { Errors = expectation.Errors, MinErrors = expectation.MinErrors, Warnings = expectation.Warnings, Codes = expectation.Codes }
                .Check(expectation.PackFile, actual, diff);

            Assert.True(
                diff.IsValid,
                expectation.PackFile + " ne se comporte plus comme son index le dit :" + Environment.NewLine
                + diff.Report() + Environment.NewLine
                + "Constats reels :" + Environment.NewLine + actual.Report());
        }
    }

    [Fact]
    public void Les_Packs_Valides_N_Ont_Aucune_Erreur()
    {
        IReadOnlyList<PackExpectation> expectations = Load();

        foreach (PackExpectation expectation in expectations.Where(e => e.MinErrors is null))
        {
            using LoadedPack pack = PackReader.Read(Path.Combine(TestPaths.FixturesDirectory, expectation.PackFile));
            ValidationResult result = PackValidator.Validate(pack, new WavAudioProbe());

            Assert.Equal(0, result.ErrorCount);
        }
    }

    [Fact]
    public void Le_Pack_De_Demonstration_N_A_Aucune_Erreure_Ni_Avertissement()
    {
        using LoadedPack pack = PackReader.Read(TestPaths.DemoPackPath);
        ValidationResult result = PackValidator.Validate(pack, new WavAudioProbe());

        Assert.True(result.IsValid, result.Report());
        Assert.Equal(0, result.WarningCount);
    }

    [Fact]
    public void Le_Pack_De_Demonstration_Contient_Les_Trois_Niveaux()
    {
        using LoadedPack pack = PackReader.Read(TestPaths.DemoPackPath);

        string[] levels =
        [
            .. pack.Charts
                .Select(chart => chart.Entry.Level)
                .Select(level => level switch
                {
                    null => string.Empty,
                    ChartLevel named => ChartLevels.FileNameOf(named),
                })
                .Order(StringComparer.Ordinal),
        ];

        Assert.Equal(ExpectedLevels, levels);
        Assert.All(pack.Charts, chart => Assert.True(chart.IsReadable, chart.Entry.File ?? "sans fichier"));
    }

    [Fact]
    public void Les_Notes_Du_Pack_De_Demonstration_Sont_Triees_Par_Temps()
    {
        using LoadedPack pack = PackReader.Read(TestPaths.DemoPackPath);

        foreach (LoadedChart loaded in pack.Charts)
        {
            IReadOnlyList<Note> notes = loaded.Chart?.Notes ?? [];
            double previous = -1d;
            foreach (Note note in notes)
            {
                Assert.True(note.Time > previous, loaded.Entry.File + " : notes non triees a " + note.Time);
                previous = note.Time;
            }
        }
    }

    [Fact]
    public void Un_Chemin_D_Echappement_Dans_L_Archive_Est_Refuse()
    {
        // « zip slip » : une entree nommee ../../etc/passwdallow de sortir de
        // l'archive. Le lecteur ne l'ouvre jamais, et le dit.
        string path = Path.Combine(TestPaths.NewTemporaryDirectory(), "evasion.btp");
        using (FileStream file = File.Create(path))
        using (System.IO.Compression.ZipArchive archive = new(file, System.IO.Compression.ZipArchiveMode.Create))
        {
            // Un manifeste minimal suffit : ce qui compte ici est l'entree qui
            // porte un chemin qui remonte hors de l'archive.
            using (StreamWriter manifest = new(archive.CreateEntry(PackFormat.ManifestFileName).Open()))
            {
                manifest.Write("""{"schema":"boutap/pack-manifest/1","id":"evasion"}""");
            }

            using StreamWriter payload = new(archive.CreateEntry("../../etc/passwd").Open());
            payload.Write("rien demeurant");
        }

        LoadedPack pack = PackReader.Read(path);
        using (pack)
        {
            ValidationResult result = PackValidator.Validate(pack);
            Assert.Contains(result.Issues, issue => issue.Code == ValidationCodes.PackEntryEscape);
        }
    }

    private static IReadOnlyList<PackExpectation> Load()
    {
        ValidationResult problems = new();
        bool loaded = PackExpectation.TryLoad(TestPaths.FixturesDirectory, out IReadOnlyDictionary<string, PackExpectation> map, problems);

        Assert.True(loaded, "index.json des fixtures illisible :" + Environment.NewLine + problems.Report());
        return [.. map.Values.OrderBy(expectation => expectation.PackFile, StringComparer.Ordinal)];
    }
}
