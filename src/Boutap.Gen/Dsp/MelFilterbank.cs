// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Boutap.Gen.Dsp;

/// <summary>
/// Les conversions entre hertz et mel, dans l'echelle de Slaney.
/// </summary>
/// <remarks>
/// L'echelle de Slaney est lineaire en dessous de 1 kHz et logarithmique
/// au-dessus, avec une continuite de derive. L'echelle HTK, elle, est
/// logarithmique partout et n'a pas de normalisation d'energie par bande. Choisir
/// la mauvaise ne « degrade » pas le resultat : cela decale toutes les bandes et
/// rend la comparaison avec l'oracle impossible.
/// </remarks>
public static class MelScale
{
    /// <summary>Frequence de raccordement, en Hz. En dessous, l'echelle est lineaire.</summary>
    public const double LinearLimitHz = 1000.0;

    /// <summary>Pas lineaire de l'echelle, en Hz par mel.</summary>
    public const double LinearStep = 200.0 / 3.0;

    /// <summary>
    /// Pas logarithmique de l'echelle : deux octaves et demie couvrent un
    /// rapport de 6,4, donc <c>ln(6,4) / 27</c> (27 tons de l'echelle mel).
    /// </summary>
    public const double LogStep = 0.06875177742094912;

    /// <summary>Convertit une frequence en mel.</summary>
    /// <param name="hertz">La frequence, en Hz.</param>
    /// <returns>La position sur l'echelle mel.</returns>
    public static double FromHertz(double hertz)
    {
        if (hertz < LinearLimitHz)
        {
            return hertz / LinearStep;
        }

        return (LinearLimitHz / LinearStep)
            + (Math.Log(hertz / LinearLimitHz) / LogStep);
    }

    /// <summary>Convertit une position mel en frequence.</summary>
    /// <param name="mel">La position sur l'echelle mel.</param>
    /// <returns>La frequence, en Hz.</returns>
    public static double ToHertz(double mel)
    {
        double linear = LinearLimitHz / LinearStep;
        if (mel < linear)
        {
            return mel * LinearStep;
        }

        return LinearLimitHz * MathExp(LogStep * (mel - linear));
    }

    /// <summary>
    /// L'exponentielle, isolee pour que la lecture du calcul reste nette.
    /// </summary>
    /// <param name="value">Le nombre.</param>
    /// <returns>Son exponentielle.</returns>
    private static double MathExp(double value) => Math.Exp(value);
}

/// <summary>
/// Un banc de filtres triangulaire mel, a energie constante.
/// </summary>
/// <remarks>
/// Chaque bande est un triangle pose sur trois bornes mel consecutives. La
/// normalisation de Slaney divise par la moitie de la largeur du triangle : sans
/// elle, les bandes larges captent une energie enorme et les bandes etroites
/// presque rien, ce qui ne dit rien de la musique.
/// </remarks>
public sealed class MelFilterbank
{
    private readonly double[] _weights;
    private readonly double[] _edges;

    private MelFilterbank(double[] weights, double[] edges, int bandCount, int binCount)
    {
        _weights = weights;
        _edges = edges;
        BandCount = bandCount;
        BinCount = binCount;
    }

    /// <summary>Nombre de bandes mel.</summary>
    public int BandCount { get; }

    /// <summary>Nombre de bins de la transformee que le banc couvre.</summary>
    public int BinCount { get; }

    /// <summary>Les bornes mel du banc, du nombre de bandes plus deux.</summary>
    public ReadOnlySpan<double> Edges => _edges;

    /// <summary>Le poids d'une bande pour un bin donne.</summary>
    /// <param name="band">Indice de bande.</param>
    /// <param name="bin">Indice de bin.</param>
    /// <returns>Le poids.</returns>
    public double Weight(int band, int bin) => _weights[(band * BinCount) + bin];

    /// <summary>Construit un banc de filtres mel.</summary>
    /// <param name="sampleRate">La frequence d'echantillonnage, en Hz.</param>
    /// <param name="fftSize">La taille de la transformee.</param>
    /// <param name="bandCount">Le nombre de bandes.</param>
    /// <param name="minimumHz">La frequence basse du banc.</param>
    /// <param name="maximumHz">La frequence haute du banc.</param>
    /// <returns>Le banc.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Un parametre est hors bornes.</exception>
    public static MelFilterbank Create(
        double sampleRate,
        int fftSize,
        int bandCount,
        double minimumHz,
        double maximumHz)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(bandCount, 1);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fftSize);
        if ((fftSize & (fftSize - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(fftSize), fftSize, "La taille de la transformee doit etre une puissance de deux.");
        }

        if (minimumHz < 0.0 || maximumHz <= minimumHz || maximumHz > (sampleRate / 2.0))
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumHz),
                maximumHz,
                "La bande du banc doit etre comprise entre 0 et la frequence de Nyquist.");
        }

        int binCount = (fftSize / 2) + 1;
        int edgeCount = bandCount + 2;
        double[] edges = new double[edgeCount];
        double lowest = MelScale.FromHertz(minimumHz);
        double highest = MelScale.FromHertz(maximumHz);
        for (int index = 0; index < edgeCount; index++)
        {
            edges[index] = MelScale.ToHertz(lowest + ((highest - lowest) * index / (edgeCount - 1)));
        }

        double[] frequencies = Frequencies(sampleRate, fftSize, binCount);
        double[] weights = new double[bandCount * binCount];
        for (int band = 0; band < bandCount; band++)
        {
            double lowerWidth = edges[band + 1] - edges[band];
            double upperWidth = edges[band + 2] - edges[band + 1];
            double normalisation = 2.0 / (edges[band + 2] - edges[band]);
            for (int bin = 0; bin < binCount; bin++)
            {
                double frequency = frequencies[bin];
                double rising = (frequency - edges[band]) / lowerWidth;
                double falling = (edges[band + 2] - frequency) / upperWidth;
                double value = Math.Max(0.0, Math.Min(rising, falling));
                weights[(band * binCount) + bin] = value * normalisation;
            }
        }

        return new MelFilterbank(weights, edges, bandCount, binCount);
    }

    /// <summary>Les frequences des bins d'une transformee reelle.</summary>
    /// <param name="sampleRate">La frequence d'echantillonnage, en Hz.</param>
    /// <param name="fftSize">La taille de la transformee.</param>
    /// <param name="binCount">Le nombre de bins.</param>
    /// <returns>Les frequences, en Hz.</returns>
    internal static double[] Frequencies(double sampleRate, int fftSize, int binCount)
    {
        double[] frequencies = new double[binCount];
        double step = sampleRate / fftSize;
        for (int bin = 0; bin < binCount; bin++)
        {
            frequencies[bin] = bin * step;
        }

        return frequencies;
    }
}
