// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Tests de la surcharge de graine. Elle ne doit toucher qu'a la graine :
// l'empreinte de l'audio est un fait, pas un choix du generateur.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Text.Json.Nodes;
using Boutap.Audio;
using Boutap.Core.Common;
using Boutap.Core.Tests;
using Boutap.Core.Determinism;
using Boutap.Core.Pack;
using Boutap.Gen.Compose;
using Xunit;

namespace Boutap.Gen.Tests;

/// <summary>Ce que fait <c>--seed</c>, et ce qu'il ne doit pas faire.</summary>
public class SeedOverrideTests
{
    private const string Version = "0.1.0";
    private static readonly string Fingerprint = new('c', 64);
    private static readonly string OtherMaterial = new('d', 64);
    private static readonly string[] Levels = ["berceau", "ronde", "cascade"];

    private static GenerationResult Generate(string? seedHex, double[]? signal = null)
    {
        double[] samples = signal ?? Clicks(seconds: 6, bpm: 120.0);
        return Pipeline.Run(
            samples, 22050, samples.Length / 22050.0, Fingerprint, Version, null, seedHex);
    }

    /// <summary>Un train de clics, construit sans generateur aleatoire.</summary>
    private static double[] Clicks(int seconds, double bpm)
    {
        double[] signal = new double[seconds * 22050];
        int step = (int)Math.Round(22050 * 60.0 / bpm);
        for (int start = 0; start < signal.Length; start += step)
        {
            for (int offset = 0; offset < 1200 && start + offset < signal.Length; offset++)
            {
                signal[start + offset] +=
                    Math.Sin((2.0 * Math.PI * 900.0 * offset) / 22050) * Math.Exp(-offset / 300.0) * 0.6;
            }
        }

        return signal;
    }

    private static PackContent Build(GenerationResult result, byte[]? audioBytes = null, GaplessTag gapless = GaplessTag.None)
    {
        var identity = new PackIdentity("essai", "Essai", string.Empty, "CC0-1.0", "audio.wav");
        return Pipeline.BuildPack(
            result, identity, audioBytes ?? [1, 2, 3, 4], gapless, SemanticVersion.Current);
    }

    private static byte[] Written(PackContent pack)
    {
        using var stream = new MemoryStream();
        PackWriter.WriteTo(stream, pack);
        return stream.ToArray();
    }

    [Fact]
    public void La_graine_du_pack_vient_du_materiau_et_non_de_l_empreinte()
    {
        PackContent pack = Build(Generate(OtherMaterial));

        Assert.Equal(
            SeedDerivation.PackSeedValue(OtherMaterial, Version),
            pack.Manifest.Generator!.Seed);
        Assert.NotEqual(SeedDerivation.PackSeedValue(Fingerprint, Version), pack.Manifest.Generator!.Seed);
    }

    [Fact]
    public void La_graine_de_chaque_niveau_vient_du_materiau_et_du_niveau()
    {
        PackContent pack = Build(Generate(OtherMaterial));

        Assert.Equal(
            string.Join("|", Levels
                .Select(level => SeedDerivation.ChartSeedValue(OtherMaterial, Version, level).ToString(CultureInfo.InvariantCulture))),
            string.Join("|", pack.Charts.Select(chart =>
                chart.Generator!.Seed!.Value.ToString(CultureInfo.InvariantCulture))));

        // Deux niveaux d'un meme morceau doivent avoir deux graines distinctes,
        // sans quoi ils se ressembleraient.
        Assert.Equal(3, pack.Charts.Select(chart => chart.Generator!.Seed!.Value).Distinct().Count());
    }

    [Fact]
    public void S_imposer_pas_de_materiau_donne_la_graine_derivee_de_l_empreinte()
    {
        PackContent pack = Build(Generate(seedHex: null));

        Assert.Equal(
            SeedDerivation.PackSeedValue(Fingerprint, Version),
            pack.Manifest.Generator!.Seed);
        Assert.All(pack.Charts, chart => Assert.Equal(
            SeedDerivation.ChartSeedValue(Fingerprint, Version, chart.Level!.Value.FileNameOf()),
            chart.Generator!.Seed));
    }

    [Fact]
    public void Le_materiau_impose_est_inscrit_pour_que_la_graine_restait_recalculable()
    {
        PackContent pack = Build(Generate(OtherMaterial));

        Assert.Equal(OtherMaterial, pack.Manifest.Generator!.Params!["seed_material"]!.GetValue<string>());

        // Sans surcharge, le materiau vaut l'empreinte, deja ecrite deux fois
        // ailleurs dans le manifeste : le redire n'ajouterait rien.
        Assert.Null(Build(Generate(seedHex: null)).Manifest.Generator!.Params!["seed_material"]);
    }

    [Fact]
    public void La_graine_imposee_change_les_notes()
    {
        // C'est tout l'interet de l'option : deux variantes du meme morceau.
        // L'ADR 0005 le promet, et le test le retient.
        string plain = Join(Build(Generate(seedHex: null)));
        string other = Join(Build(Generate(OtherMaterial)));

        Assert.NotEqual(plain, other);
    }

    [Fact]
    public void Deux_materiaux_identiques_donnent_le_meme_pack_octet_pour_octet()
    {
        byte[] first = Written(Build(Generate(OtherMaterial)));
        byte[] second = Written(Build(Generate(OtherMaterial)));

        Assert.Equal(first, second);
        Assert.NotEqual(first, Written(Build(Generate(seedHex: null))));
    }

    /// <summary>Le reel defaut : l'empreinte ne bouge pas, meme graine imposee.</summary>
    [Fact]
    public void L_empreinte_de_l_audio_ne_bouge_pas()
    {
        PackContent pack = Build(Generate(OtherMaterial));

        Assert.Equal(Fingerprint, pack.Manifest.Audio!.Sha256);
        Assert.All(pack.Charts, chart => Assert.Equal(Fingerprint, chart.AudioSha256));

        // C'est exactement le defaut corrige : avant, la graine impose ecrivait
        // sa valeur dans ces deux champs, et le pack sortait de la validation
        // en erreur audio.hash-mismatch.
        Assert.NotEqual(OtherMaterial, pack.Manifest.Audio!.Sha256);
    }

    [Fact]
    public void Un_pack_a_graine_imposee_passe_la_validation()
    {
        // Un vrai WAV, pas quatre octets : la regle audio.hash-mismatch ne peut
        // recaler l'empreinte que si l'audio du pack se decode reellement. Or
        // c'est precisement elle qui avait refuse le pack avant le correctif.
        string[] codes = ErrorsOf(BuildOnDemoAudio(OtherMaterial), "graine.btp");
        Assert.True(
            codes.Length == 0,
            "Le pack a graine imposee doit rester conforme au format ; le validateur trouve : "
            + string.Join(" | ", codes) + ".");
    }

    [Fact]
    public void Un_materiau_inscrit_qui_ne_dit_pas_la_vraie_graine_est_refuse()
    {
        // La regle de derivation doit rester vivante. Si le validateur acceptait
        // n'importe quel materiau, elle ne prouverait plus rien.
        string directory = TestPaths.NewTemporaryDirectory();
        try
        {
            PackContent pack = BuildOnDemoAudio(OtherMaterial);
            GeneratorInfo generator = pack.Manifest.Generator!;
            var forged = new JsonObject();
            foreach (KeyValuePair<string, JsonNode?> entry in generator.Params!)
            {
                forged[entry.Key] = entry.Value?.DeepClone();
            }

            forged["seed_material"] = new string('f', 64);
            PackContent forgee = pack with
            {
                Manifest = pack.Manifest with
                {
                    Generator = generator with { Params = forged },
                },
            };

            Assert.Equal(
                "manifest.seed-not-derived|manifest.seed-not-derived|manifest.seed-not-derived",
                string.Join("|", ErrorsOf(forgee, "forge.btp")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Un_materiau_absent_ou_invalide_retombe_sur_l_empreinte()
    {
        // Un champ absent, un champ qui n'est pas une chaine, un champ qui
        // n'est pas de l'hex : dans les trois cas on ne peut rien recalculer
        // depuis le materiau, et l'empreinte reste la seule source possible.
        foreach (JsonNode? bogus in new JsonNode?[] { null, new JsonArray(), JsonValue.Create(12) })
        {
            PackContent pack = BuildOnDemoAudio(OtherMaterial);
            GeneratorInfo generator = pack.Manifest.Generator!;
            var parameters = new JsonObject();
            foreach (KeyValuePair<string, JsonNode?> entry in generator.Params!)
            {
                parameters[entry.Key] = entry.Value?.DeepClone();
            }

            parameters["seed_material"] = bogus;
            PackContent forged = pack with
            {
                Manifest = pack.Manifest with
                {
                    Generator = generator with { Params = parameters },
                },
            };

            string codes = string.Join("|", ErrorsOf(forged, "invalide.btp"));
            Assert.True(
                codes.Contains("manifest.seed-not-derived", StringComparison.Ordinal),
                "Le materiau « " + (bogus?.ToJsonString() ?? "<absent>")
                + " » aurait du etre ignore ; les codes d'ecreur sont : " + codes + ".");
        }
    }

    /// <summary>Un pack genere sur le vrai audio de demonstration.</summary>
    private static PackContent BuildOnDemoAudio(string? seedHex)
    {
        double[] samples = DemoAudio(out byte[] canonical, out int rate, out double duration, out string fingerprint, out GaplessTag gapless);
        GenerationResult result = Pipeline.Run(samples, rate, duration, fingerprint, Version, null, seedHex);
        return Build(result, canonical, gapless);
    }

    /// <summary>Les codes d'erreur que le validateur trouve dans un pack.</summary>
    private static string[] ErrorsOf(PackContent pack, string fileName)
    {
        string directory = TestPaths.NewTemporaryDirectory();
        try
        {
            string path = Path.Combine(directory, fileName);
            PackWriter.Write(path, pack);
            using LoadedPack loaded = PackReader.Read(path);
            return PackValidator.Validate(loaded, new WavAudioProbe()).Issues
                .Where(issue => issue.IsError)
                .Select(issue => issue.Code)
                .ToArray();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// L'audio du pack de demonstration, lu sur place : ni duplique dans les
    /// donnees de test, ni reencode.
    /// </summary>
    private static double[] DemoAudio(
        out byte[] canonical,
        out int sampleRate,
        out double duration,
        out string fingerprint,
        out GaplessTag gapless)
    {
        using LoadedPack pack = PackReader.Read(TestPaths.DemoPackPath);
        Manifest manifest = pack.Manifest;
        AudioRef audio = manifest.Audio ?? throw new InvalidOperationException("Le pack n'a pas d'audio.");
        string entryName = audio.Path ?? throw new InvalidOperationException("Audio sans chemin.");

        // Les octets stockes tels quels, lus dans une passe a part : le decodeur
        // laisse le flux a la fin, et copier ensuite ne donnerait que zero octet.
        // C'est deja un WAV, et le hacher comme le ferait le validateur evite
        // de dependre d'un encodeur.
        using (Stream raw = pack.OpenEntry(entryName)
            ?? throw new InvalidOperationException("L'entree audio ne s'ouvre pas."))
        using (var buffer = new MemoryStream())
        {
            raw.CopyTo(buffer);
            canonical = buffer.ToArray();
        }

        using Stream stream = pack.OpenEntry(entryName)
            ?? throw new InvalidOperationException("L'entree audio ne s'ouvre pas.");
        WavInfo info = WavDecoder.ReadInfo(stream);
        float[] decoded = WavDecoder.DecodeToCanonical(stream, info);

        sampleRate = info.SampleRate;
        duration = decoded.Length / (double)info.SampleRate;
        fingerprint = audio.Sha256 ?? throw new InvalidOperationException("Audio sans empreinte.");
        gapless = WavGapless.Read(canonical);
        return Array.ConvertAll(decoded, value => (double)value);
    }

    private static string Join(PackContent pack) => string.Join(
        "|",
        pack.Charts.Select(chart => string.Join(
            ",",
            chart.Notes!.Select(note => note.Time.ToString("0.000000", CultureInfo.InvariantCulture)))));
}
