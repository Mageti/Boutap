// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Boutap.Gen.Dsp;

namespace Boutap.Gen.Analysis;

/// <summary>
/// Tout ce qu'une analyse spectrale produit, en une fois.
/// </summary>
/// <param name="SampleRate">La frequence d'echantillonnage, en Hz.</param>
/// <param name="Spectrogram">Le module du spectrogramme court terme.</param>
/// <param name="Mel">La puissance par bande mel, trame par trame.</param>
/// <param name="Chroma">La couleur par classe de hauteur, trame par trame.</param>
/// <param name="Onset">L'enveloppe d'onsets, une valeur par trame.</param>
public sealed record SpectralAnalysis(
    double SampleRate,
    MagnitudeSpectrogram Spectrogram,
    double[] Mel,
    double[] Chroma,
    double[] Onset)
{
    /// <summary>Nombre de trames.</summary>
    public int FrameCount => Spectrogram.FrameCount;

    /// <summary>Nombre de bandes mel.</summary>
    public static int BandCount => AnalysisSettings.MelBands;

    /// <summary>Nombre de classes de hauteur.</summary>
    public static int ClassCount => AnalysisSettings.ChromaBins;
}

/// <summary>
/// Le point d'entree de l'analyse.
/// </summary>
public static class SpectralAnalyzer
{
    /// <summary>
    /// Analyse un signal en mel, chroma et enveloppe d'onsets.
    /// </summary>
    /// <param name="samples">Le signal, en echantillons dans <c>[-1 ; 1]</c>.</param>
    /// <param name="sampleRate">La frequence d'echantillonnage, en Hz.</param>
    /// <returns>L'analyse complete.</returns>
    /// <exception cref="ArgumentException">Le signal est vide ou la frequence est invalide.</exception>
    /// <remarks>
    /// Les bandes mel couvrent toute la largeur de bande jusqu'a Nyquist. Les
    /// coupes basses et hautes de la carte de sons ne viennent qu'apres, au
    /// moment de la composition, ou elles ont une signification musicale.
    /// </remarks>
    public static SpectralAnalysis Analyze(ReadOnlySpan<double> samples, double sampleRate)
    {
        if (samples.Length == 0)
        {
            throw new ArgumentException("Le signal est vide.", nameof(samples));
        }

        if (sampleRate <= AnalysisSettings.Nfft)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sampleRate),
                sampleRate,
                $"La frequence d'echantillonnage doit depasser {AnalysisSettings.Nfft} Hz pour que la transformee ait du sens.");
        }

        Fft fft = new(AnalysisSettings.Nfft);
        double[] window = Stft.HannWindow(AnalysisSettings.WindowLength);
        MagnitudeSpectrogram spectrogram = Stft.Compute(samples, fft, window);

        MelFilterbank mel = MelFilterbank.Create(
            sampleRate, AnalysisSettings.Nfft, AnalysisSettings.MelBands, 0.0, sampleRate / 2.0);
        ChromaFilterbank chroma = ChromaFilterbank.Create(
            sampleRate, AnalysisSettings.Nfft, AnalysisSettings.ChromaBins, AnalysisSettings.Tuning);

        double[] melSpectrogram = Spectrograms.Mel(spectrogram, mel);
        double[] chromaSpectrogram = Spectrograms.Chroma(spectrogram, chroma);
        double[] onset = OnsetEnvelope.Compute(melSpectrogram, AnalysisSettings.MelBands, spectrogram.FrameCount);

        return new SpectralAnalysis(sampleRate, spectrogram, melSpectrogram, chromaSpectrogram, onset);
    }
}
