// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Boutap.Audio;
using Boutap.Core.Pack;
using Boutap.Core.Tests;
using Boutap.Gen.Analysis;
using Boutap.Gen.Dsp;
using Xunit;

namespace Boutap.Gen.Tests;

/// <summary>
/// La comparaison du portage C# avec la reference <c>tests/data/goldens</c>.
/// </summary>
/// <remarks>
/// <para>
/// La reference a ete produite par <c>tools/make-goldens.py</c>, qui transcrit
/// les formules de librosa 0.10.2 en double precision, puis verifie que cette
/// transcription ne s'ecarte pas de librosa lui-meme de plus de 1e-9. Le test
/// ci-dessous verifie la deuxieme moitie du contrat : que le C# colle a la
/// transcription.
/// </para>
/// <para>
/// Deux niveaux de controle, parce qu'ils attrapent deux familles de bugs
/// differentes. La comparaison des lignes stockees dit « la valeur est fausse »
/// et localise le desaccord. La comparaison des empreintes dit « une des
/// 6 x 128, ou 6 x 12, ou 862 valeurs est fausse » sans avoir a deviner
/// laquelle.
/// </para>
/// </remarks>
public sealed class GoldenAnalysisTests
{
    private const double Tolerance = 1e-6;
    private const double AbsoluteTolerance = 1e-14;
    private static string GoldenPath =>
        Path.Combine(TestPaths.RepositoryRoot, "tests", "data", "goldens", "analysis-golden.json");

    private static JsonDocument Golden() => JsonDocument.Parse(File.ReadAllText(GoldenPath));

    private static SpectralAnalysis AnalyzeDemo(out double sampleRate, out int sampleCount)
    {
        double[] samples = DemoAudio(out sampleRate, out sampleCount);
        return SpectralAnalyzer.Analyze(samples, sampleRate);
    }

    /// <summary>Sort l'audio du pack de demonstration, sans le dupliquer dans les donnees de test.</summary>
    private static double[] DemoAudio(out double sampleRate, out int sampleCount)
    {
        using LoadedPack pack = PackReader.Read(TestPaths.DemoPackPath);
        Manifest manifest = Require(pack.Manifest);
        AudioRef audio = Require(manifest.Audio);
        string entryName = Require(audio.Path);
        using Stream stream = Require(pack.OpenEntry(entryName));
        WavInfo info = WavDecoder.ReadInfo(stream);
        float[] samples = WavDecoder.DecodeToCanonical(stream, info);
        sampleRate = info.SampleRate;
        sampleCount = samples.Length;
        return Array.ConvertAll(samples, value => (double)value);
    }

    private static T Require<T>(T? value)
        where T : class
    {
        Assert.NotNull(value);
        return value;
    }

    [Fact]
    public void La_reference_decrit_le_meme_signal()
    {
        using JsonDocument golden = Golden();
        JsonElement source = golden.RootElement.GetProperty("source");
        SpectralAnalysis analysis = AnalyzeDemo(out double sampleRate, out int sampleCount);

        Assert.Equal(source.GetProperty("sample_rate").GetDouble(), sampleRate);
        Assert.Equal(source.GetProperty("sample_count").GetInt32(), sampleCount);
        Assert.Equal(
            source.GetProperty("duration_seconds").GetDouble(),
            sampleCount / sampleRate,
            6);
        Assert.Equal(1, analysis.Spectrogram.BinCount - (AnalysisSettings.Nfft / 2));
    }

    [Fact]
    public void Le_nombre_de_trames_et_la_taille_des_bancs_sont_celles_de_la_reference()
    {
        using JsonDocument golden = Golden();
        SpectralAnalysis analysis = AnalyzeDemo(out _, out _);

        Assert.Equal(
            golden.RootElement.GetProperty("frames").GetProperty("stft_columns").GetInt32(),
            analysis.FrameCount);
        Assert.Equal(AnalysisSettings.MelBands, SpectralAnalysis.BandCount);
        Assert.Equal(AnalysisSettings.ChromaBins, SpectralAnalysis.ClassCount);
        Assert.Equal(analysis.FrameCount * SpectralAnalysis.BandCount, analysis.Mel.Length);
        Assert.Equal(analysis.FrameCount * SpectralAnalysis.ClassCount, analysis.Chroma.Length);
        Assert.Equal(analysis.FrameCount, analysis.Onset.Length);
    }

    [Fact]
    public void Le_mel_colle_a_la_reference_ligne_a_ligne()
    {
        using JsonDocument golden = Golden();
        SpectralAnalysis analysis = AnalyzeDemo(out _, out _);

        CompareRows(
            analysis.Mel,
            golden.RootElement.GetProperty("mel").GetProperty("rows"),
            SpectralAnalysis.BandCount);
    }

    [Fact]
    public void Le_chroma_colle_a_la_reference_ligne_a_ligne()
    {
        using JsonDocument golden = Golden();
        SpectralAnalysis analysis = AnalyzeDemo(out _, out _);

        CompareRows(
            analysis.Chroma,
            golden.RootElement.GetProperty("chroma").GetProperty("rows"),
            SpectralAnalysis.ClassCount);
    }

    [Fact]
    public void Les_bornes_mel_de_la_reference_sont_bien_reproduites()
    {
        using JsonDocument golden = Golden();
        SpectralAnalysis analysis = AnalyzeDemo(out _, out _);
        MelFilterbank bank = MelFilterbank.Create(
            analysis.SampleRate, AnalysisSettings.Nfft, AnalysisSettings.MelBands, 0.0, analysis.SampleRate / 2.0);
        JsonElement edges = golden.RootElement.GetProperty("mel").GetProperty("band_center_hz");

        Assert.Equal(edges.GetArrayLength(), bank.Edges.Length);
        for (int index = 0; index < edges.GetArrayLength(); index++)
        {
            AssertClose(edges[index].GetDouble(), bank.Edges[index], 0.0, $"borne mel {index}");
        }
    }

    [Fact]
    public void L_enveloppe_d_onsets_colle_a_la_reference()
    {
        using JsonDocument golden = Golden();
        SpectralAnalysis analysis = AnalyzeDemo(out _, out _);
        JsonElement section = golden.RootElement.GetProperty("onset");
        JsonElement values = section.GetProperty("values");

        Assert.Equal(section.GetProperty("length").GetInt32(), analysis.Onset.Length);
        for (int index = 0; index < values.GetArrayLength(); index++)
        {
            AssertClose(values[index].GetDouble(), analysis.Onset[index], AbsoluteTolerance, $"enveloppe {index}");
        }
    }

    [Fact]
    public void Les_empreintes_du_mel_et_du_chroma_sont_identiques()
    {
        using JsonDocument golden = Golden();
        SpectralAnalysis analysis = AnalyzeDemo(out _, out _);
        int[] rows = CheckedRows(golden.RootElement);

        Assert.Equal(
            golden.RootElement.GetProperty("mel").GetProperty("digest").GetString(),
            Digest(analysis.Mel, SpectralAnalysis.BandCount, rows));
        Assert.Equal(
            golden.RootElement.GetProperty("chroma").GetProperty("digest").GetString(),
            Digest(analysis.Chroma, SpectralAnalysis.ClassCount, rows));
    }

    [Fact]
    public void L_empreinte_de_l_enveloppe_est_identique()
    {
        using JsonDocument golden = Golden();
        SpectralAnalysis analysis = AnalyzeDemo(out _, out _);

        Assert.Equal(
            golden.RootElement.GetProperty("onset").GetProperty("digest").GetString(),
            Digest(analysis.Onset, 1, Enumerable.Range(0, analysis.FrameCount).ToArray()));
    }

    [Fact]
    public void L_enveloppe_d_onsets_detecte_des_onsets_et_survit_au_silence()
    {
        SpectralAnalysis analysis = AnalyzeDemo(out _, out _);
        int silent = analysis.Onset.Count(value => value <= 0.0);

        // Le morceau de demonstration est volontairement tres aere : quatre-vingts
        // pour cent de silence entre les notes. Ce qui compte n'est donc pas la
        // densite, mais le fait que l'enveloppe ne soit pas plate et que le
        // retard de bord soit rebouche.
        Assert.True(
            analysis.Onset.Count(value => value > 0.0) > 50,
            $"L'enveloppe ne compte que {analysis.Onset.Length - silent} valeur(s) positive(s) : elle ne detecte rien.");
        Assert.True(
            analysis.Onset[0] == 0.0 && analysis.Onset[1] == 0.0 && analysis.Onset[2] == 0.0,
            "Les trois premieres valeurs devraient couvrir le retard de bord, pas un onset.");
        Assert.Equal(
            0.0,
            analysis.Onset.Min(),
            6);
    }

    [Fact]
    public void L_analyse_est_deterministe()
    {
        SpectralAnalysis first = AnalyzeDemo(out _, out _);
        SpectralAnalysis second = AnalyzeDemo(out _, out _);

        Assert.Equal(Digest(first.Mel, SpectralAnalysis.BandCount, Enumerable.Range(0, first.FrameCount).ToArray()),
            Digest(second.Mel, SpectralAnalysis.BandCount, Enumerable.Range(0, second.FrameCount).ToArray()));
        Assert.Equal(first.Onset, second.Onset);
    }

    private static int[] CheckedRows(JsonElement golden)
    {
        int[] mel = golden.GetProperty("frames").GetProperty("mel_rows_checked")
            .EnumerateArray().Select(value => value.GetInt32()).ToArray();
        int[] chroma = golden.GetProperty("frames").GetProperty("chroma_rows_checked")
            .EnumerateArray().Select(value => value.GetInt32()).ToArray();
        Assert.Equal(mel, chroma);
        return mel;
    }

    private static void CompareRows(double[] actual, JsonElement expected, int width)
    {
        using JsonDocument golden = Golden();
        int[] rows = CheckedRows(golden.RootElement);
        for (int row = 0; row < rows.Length; row++)
        {
            JsonElement line = expected[row];
            Assert.Equal(width, line.GetArrayLength());
            for (int column = 0; column < width; column++)
            {
                AssertClose(
                    line[column].GetDouble(),
                    actual[(rows[row] * width) + column],
                    AbsoluteTolerance,
                    $"trame {rows[row]}, colonne {column}");
            }
        }
    }

    private static void AssertClose(double expected, double actual, double absolute, string label)
    {
        double allowed = Math.Max(absolute, Math.Abs(expected) * Tolerance);
        Assert.True(
            Math.Abs(expected - actual) <= allowed,
            $"{label} : reference {expected:R}, obtenu {actual:R}, ecart {Math.Abs(expected - actual):E3} > {allowed:E3}");
    }

    /// <summary>
    /// L'empreinte grossiere du script de reference, reimplementee a l'identique.
    /// </summary>
    /// <param name="values">Le tableau, trame par trame.</param>
    /// <param name="width">Le nombre de colonnes d'une trame.</param>
    /// <param name="rows">Les trames a inclure.</param>
    /// <returns>L'empreinte hexadecimale.</returns>
    private static string Digest(double[] values, int width, int[] rows)
    {
        StringBuilder payload = new();
        bool first = true;
        foreach (int row in rows)
        {
            for (int column = 0; column < width; column++)
            {
                if (!first)
                {
                    payload.Append(',');
                }

                first = false;
                payload.Append(
                    values[(row * width) + column].ToString("0.000000e+00", CultureInfo.InvariantCulture));
            }
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(payload.ToString())))
            .ToLowerInvariant();
    }
}
