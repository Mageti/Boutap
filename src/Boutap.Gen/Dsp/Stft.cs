// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Boutap.Gen.Dsp;

/// <summary>
/// Le module du spectrogramme de puissance court terme.
/// </summary>
/// <remarks>
/// Le module est stocke trame par trame, chaque trame Tenant tous les bins.
/// C'est l'ordre de lecture du mel et du chroma : on balaye les bandes d'une
/// trame entiere avant de passer a la suivante, donc on lit la memoire dans le
/// sens.
/// </remarks>
public sealed class MagnitudeSpectrogram
{
    private readonly double[] _magnitudes;

    /// <summary>Construit un spectrogramme a partir d'un tableau deja rempli.</summary>
    /// <param name="magnitudes">Le module, trame par trame.</param>
    /// <param name="frameCount">Nombre de trames.</param>
    /// <param name="binCount">Nombre de bins par trame.</param>
    /// <exception cref="ArgumentNullException">Le tableau est nul.</exception>
    /// <exception cref="ArgumentException">Le tableau ne contient pas trames x bins valeurs.</exception>
    public MagnitudeSpectrogram(double[] magnitudes, int frameCount, int binCount)
    {
        ArgumentNullException.ThrowIfNull(magnitudes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(binCount);
        if (magnitudes.Length != frameCount * binCount)
        {
            throw new ArgumentException(
                $"Le tableau contient {magnitudes.Length} valeurs, il en faut {frameCount * binCount}.",
                nameof(magnitudes));
        }

        _magnitudes = magnitudes;
        FrameCount = frameCount;
        BinCount = binCount;
    }

    /// <summary>Nombre de trames.</summary>
    public int FrameCount { get; }

    /// <summary>Nombre de bins par trame, zone de Nyquist comprise.</summary>
    public int BinCount { get; }

    /// <summary>Le module brut du tableau, sans copie.</summary>
    public ReadOnlySpan<double> Raw => _magnitudes;

    /// <summary>Le module d'un coefficient.</summary>
    /// <param name="frame">Indice de trame.</param>
    /// <param name="bin">Indice de bin.</param>
    /// <returns>Le module.</returns>
    public double Magnitude(int frame, int bin) => _magnitudes[(frame * BinCount) + bin];

    /// <summary>La puissance d'un coefficient, c'est-a-dire son module carre.</summary>
    /// <param name="frame">Indice de trame.</param>
    /// <param name="bin">Indice de bin.</param>
    /// <returns>La puissance.</returns>
    public double Power(int frame, int bin)
    {
        double value = _magnitudes[(frame * BinCount) + bin];
        return value * value;
    }
}

/// <summary>
/// La transformee de Fourier courte, au sens de librosa.
/// </summary>
public static class Stft
{
    /// <summary>
    /// Calcule le module du spectrogramme d'un signal.
    /// </summary>
    /// <param name="samples">Le signal, en echantillons.</param>
    /// <param name="fft">Le transformeur, de taille <see cref="AnalysisSettings.Nfft"/>.</param>
    /// <param name="window">La fenetre, de taille <see cref="AnalysisSettings.WindowLength"/>.</param>
    /// <returns>Le spectrogramme.</returns>
    /// <exception cref="ArgumentNullException">Un parametre de reference est nul.</exception>
    /// <exception cref="ArgumentException">Le signal est vide ou la fenetre mal dimensionnee.</exception>
    public static MagnitudeSpectrogram Compute(
        ReadOnlySpan<double> samples,
        Fft fft,
        ReadOnlySpan<double> window)
    {
        ArgumentNullException.ThrowIfNull(fft);

        int size = AnalysisSettings.Nfft;
        if (samples.Length == 0)
        {
            throw new ArgumentException("Le signal est vide.", nameof(samples));
        }

        if (window.Length != size)
        {
            throw new ArgumentException(
                $"La fenetre doit avoir {size} echantillons, elle en a {window.Length}.",
                nameof(window));
        }

        int frameCount = AnalysisSettings.FrameCount(samples.Length);
        int binCount = size / 2 + 1;
        double[] magnitudes = new double[frameCount * binCount];
        double[] frame = new double[size];

        for (int index = 0; index < frameCount; index++)
        {
            int origin = (index * AnalysisSettings.HopLength) - (size / 2);
            for (int offset = 0; offset < size; offset++)
            {
                frame[offset] = Reflect(samples, origin + offset) * window[offset];
            }

            fft.Magnitudes(frame, magnitudes.AsSpan((index * binCount), binCount));
        }

        return new MagnitudeSpectrogram(magnitudes, frameCount, binCount);
    }

    /// <summary>
    /// La fenetre de Hann periodique, telle que <c>scipy.signal.get_window</c> la
    /// calcule pour une STFT centree.
    /// </summary>
    /// <param name="length">Nombre d'echantillons.</param>
    /// <returns>La fenetre.</returns>
    /// <remarks>
    /// Periodique et non symetrique : la formule est
    /// <c>0,5 - 0,5 cos(2 pi n / N)</c> sans le denominateur <c>N - 1</c>. Un
    /// Hann symetrique decalerait la reponse impulsionnelle d'un echantillon et
    /// tous les bins avec, ce que le test golden attraperait.
    /// </remarks>
    public static double[] HannWindow(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);
        double[] window = new double[length];
        for (int index = 0; index < length; index++)
        {
            window[index] = 0.5 - (0.5 * Math.Cos((2.0 * Math.PI * index) / length));
        }

        return window;
    }

    /// <summary>
    /// L'echantillon obtenu en reflechissant le signal autour de son bord, comme
    /// le remplissage <c>reflect</c> de NumPy.
    /// </summary>
    /// <param name="samples">Le signal.</param>
    /// <param name="index">L'indice voulue, eventuellement hors bornes.</param>
    /// <returns>L'echantillon reflechi.</returns>
    private static double Reflect(ReadOnlySpan<double> samples, long index)
    {
        int count = samples.Length;
        if (index >= 0 && index < count)
        {
            return samples[(int)index];
        }

        if (count == 1)
        {
            return samples[0];
        }

        long period = 2L * (count - 1);
        long position = index % period;
        if (position < 0)
        {
            position += period;
        }

        return samples[(int)(position < count ? position : period - position)];
    }
}
