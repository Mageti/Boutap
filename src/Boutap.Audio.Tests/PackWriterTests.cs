// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text;
using Boutap.Core.Pack;
using Boutap.Core.Tests;
using Xunit;

namespace Boutap.Audio.Tests;

/// <summary>
/// Ecriture d'un pack. Deux exigences, une seule fois ecrite :
/// le meme contenu doit produire le meme octet, et ce qui est ecrit doit
/// pouvoir etre relu par le validateur sans une seule erreur.
/// </summary>
public sealed class PackWriterTests
{
    [Fact]
    public void Deux_Ecritures_Du_Meme_Contenu_Donnent_Le_Meme_Octet()
    {
        string directory = TestPaths.NewTemporaryDirectory();
        try
        {
            PackContent content = ReadDemoContent();

            byte[] first = Write(Path.Combine(directory, "premier.btp"), content);
            byte[] second = Write(Path.Combine(directory, "second.btp"), content);

            Assert.Equal(first, second);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Le_Pack_Ecrit_Reste_Valide()
    {
        // C'est le test qui compte le plus : il passe par le modele, donc il
        // echoue des que le serialiseur emet un champ que le schema refuse.
        string directory = TestPaths.NewTemporaryDirectory();
        try
        {
            string path = Path.Combine(directory, "reecrit.btp");
            Write(path, ReadDemoContent());

            using LoadedPack pack = PackReader.Read(path);
            ValidationResult result = PackValidator.Validate(pack, new WavAudioProbe());

            Assert.True(result.IsValid, result.Report());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Le_Pack_Ecrit_N_Contient_Que_Les_Entrees_Attendues()
    {
        string directory = TestPaths.NewTemporaryDirectory();
        try
        {
            string path = Path.Combine(directory, "entrees.btp");
            Write(path, ReadDemoContent());

            using LoadedPack pack = PackReader.Read(path);

            string[] expected =
            [
                "LICENSE.txt", "REPORT.txt", "audio.wav",
                "charts/berceau.json", "charts/cascade.json", "charts/ronde.json",
                "manifest.json",
            ];

            Assert.Equal(expected, pack.Entries.Order(StringComparer.Ordinal).ToArray());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void L_Ordre_Du_Manifeste_Est_Celu_Des_Entrees()
    {
        // L'ordre des entrees est fixe parce que ZipArchive ne garantit rien
        // sur son parcours : le lecteur doit pouvoir faire confiance a
        // l'ordre du manifeste.
        string directory = TestPaths.NewTemporaryDirectory();
        try
        {
            string path = Path.Combine(directory, "ordre.btp");
            Write(path, ReadDemoContent());

            using LoadedPack pack = PackReader.Read(path);
            int previous = -1;
            foreach (LoadedChart chart in pack.Charts)
            {
                string file = chart.Entry.File!;
                int position = pack.Entries.ToList().IndexOf(file);
                Assert.True(position > previous, file + " n'est pas apres l'entree precedente.");
                previous = position;
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void L_Ecriture_Est_Atomique()
    {
        string directory = TestPaths.NewTemporaryDirectory();
        try
        {
            string path = Path.Combine(directory, "atomique.btp");
            Write(path, ReadDemoContent());

            Assert.True(File.Exists(path));
            Assert.False(File.Exists(path + PackWriter.TemporarySuffix));
            Assert.Empty(Directory.GetFiles(directory, "*" + PackWriter.TemporarySuffix));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Un_Fichier_Existant_Est_Remplace()
    {
        string directory = TestPaths.NewTemporaryDirectory();
        try
        {
            string path = Path.Combine(directory, "existant.btp");
            File.WriteAllText(path, "ancien contenu, sans rapport");

            byte[] written = Write(path, ReadDemoContent());

            Assert.Equal(written, File.ReadAllBytes(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Un_Chemin_D_Audio_Qui_Sort_Du_Pack_Est_Refuse()
    {
        PackContent content = ReadDemoContent() with { AudioPath = "../evasion.wav" };

        // Un chemin qui sort du pack est un probleme de format, pas d'appel :
        // PackFormatException dit ce qui ne va pas, ArgumentException dirait
        // seulement que l'appelant a mal compile.
        PackFormatException error = Assert.Throws<PackFormatException>(
            () => PackWriter.Write(Path.Combine(TestPaths.NewTemporaryDirectory(), "refuse.btp"), content));

        Assert.Contains("..", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Un_Rapport_Est_Ecrit_Sous_Son_Nom_De_Convention()
    {
        string directory = TestPaths.NewTemporaryDirectory();
        try
        {
            string path = Path.Combine(directory, "rapport.btp");
            Write(path, ReadDemoContent() with { Report = "rapport de test\n" });

            using LoadedPack pack = PackReader.Read(path);
            using Stream? report = pack.OpenEntry(PackFormat.ReportFileName);
            using StreamReader reader = new(report!, Encoding.UTF8);

            Assert.Equal("rapport de test\n", reader.ReadToEnd());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Un_Rapport_Absent_N_Ecrit_Pas_Une_Entree_Vide()
    {
        string directory = TestPaths.NewTemporaryDirectory();
        try
        {
            string path = Path.Combine(directory, "sans-rapport.btp");
            Write(path, ReadDemoContent() with { Report = null });

            using LoadedPack pack = PackReader.Read(path);

            Assert.False(pack.HasEntry(PackFormat.ReportFileName));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static byte[] Write(string path, PackContent content)
    {
        PackWriter.Write(path, content);
        return File.ReadAllBytes(path);
    }

    private static PackContent ReadDemoContent()
    {
        using LoadedPack pack = PackReader.Read(TestPaths.DemoPackPath);

        string audioPath = pack.Manifest.Audio?.Path
            ?? throw new InvalidOperationException("le pack de demonstration n'a pas d'audio");

        List<Chart> charts = [];
        foreach (LoadedChart loaded in pack.Charts)
        {
            charts.Add(loaded.Chart ?? throw new InvalidOperationException(loaded.Entry.File + " est illisible"));
        }

        return new PackContent
        {
            Manifest = pack.Manifest,
            Charts = charts,
            AudioPath = audioPath,
            AudioBytes = ReadEntry(pack, audioPath),
            Report = ReadText(pack, PackFormat.ReportFileName),
            LicenseText = ReadText(pack, PackFormat.LicenseFileName),
        };
    }

    private static byte[] ReadEntry(LoadedPack pack, string entry)
    {
        using Stream? stream = pack.OpenEntry(entry);
        if (stream is null)
        {
            throw new InvalidOperationException("entree absente : " + entry);
        }

        using MemoryStream buffer = new();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static string? ReadText(LoadedPack pack, string entry)
    {
        using Stream? stream = pack.OpenEntry(entry);
        if (stream is null)
        {
            return null;
        }

        using StreamReader reader = new(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
