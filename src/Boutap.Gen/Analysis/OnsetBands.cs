// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Boutap.Gen.Dsp;

namespace Boutap.Gen.Analysis;

/// <summary>Flux d'onsets bande par bande, avant toute reduction.</summary>
/// <remarks>
/// <para>
/// La formulation d'Ellis (2007) travaille sur une enveloppe 1D, mais le score
/// local du suivi de battement a besoin de plus : il faut savoir <em>quelles</em>
/// bandes se sont allumees, pas seulement combien. Cette classe expose donc le
/// flux non reduit, et <see cref="OnsetEnvelope"/> se contente d'en prendre la
/// moyenne.
/// </para>
/// <para>
/// L'orientation est trame par trame, comme le reste du projet :
/// <c>values[frame * bandCount + band]</c>.
/// </para>
/// </remarks>
public sealed class OnsetBands
{
    private OnsetBands(double[] values, int bandCount, int frameCount, double hopSeconds)
    {
        Values = values;
        BandCount = bandCount;
        FrameCount = frameCount;
        HopSeconds = hopSeconds;
    }

    /// <summary>Flux redresse, non reduit.</summary>
    public double[] Values { get; }

    /// <summary>Nombre de bandes mel.</summary>
    public int BandCount { get; }

    /// <summary>Nombre de trames.</summary>
    public int FrameCount { get; }

    /// <summary>Durée couverte par un saut de trame, en secondes.</summary>
    public double HopSeconds { get; }

    /// <summary>Valeur du flux d'une bande a une trame.</summary>
    /// <param name="frame">Index de trame.</param>
    /// <param name="band">Index de bande mel.</param>
    /// <returns>Le flux, en decibels, toujours positif ou nul.</returns>
    public double At(int frame, int band) => Values[(frame * BandCount) + band];

    /// <summary>Moyenne du flux sur toutes les bandes, pour une trame.</summary>
    /// <param name="frame">Index de trame.</param>
    /// <returns>La moyenne des bandes de cette trame.</returns>
    public double ColumnMean(int frame)
    {
        double sum = 0.0;
        for (int band = 0; band < BandCount; band++)
        {
            sum += Values[(frame * BandCount) + band];
        }

        return sum / BandCount;
    }

    /// <summary>Calcule le flux bande par bande a partir du mel lineaire.</summary>
    /// <param name="mel">Mel lineaire, trame par trame.</param>
    /// <param name="bandCount">Nombre de bandes.</param>
    /// <param name="frameCount">Nombre de trames.</param>
    /// <param name="hopSeconds">Duree d'une trame, en secondes.</param>
    /// <returns>Le flux redresse.</returns>
    /// <remarks>
    /// L'ordre suit <c>onset.onset_strength_multi</c> a la lettre : passage en
    /// decibels, difference decalee, redressement. Le decalage de
    /// <see cref="AnalysisSettings.OnsetLag"/> trames est reporte en fin de
    /// boucle, pour que la trame <c>i</c> porte le flux qui part vers <c>i</c> et
    /// non celui qui arrive de <c>i-1</c>.
    /// </remarks>
    /// <exception cref="ArgumentException">Le mel est vide ou mal forme.</exception>
    public static OnsetBands Compute(double[] mel, int bandCount, int frameCount, double hopSeconds)
    {
        ArgumentNullException.ThrowIfNull(mel);
        if (bandCount < 1)
        {
            throw new ArgumentException("Il faut au moins une bande.", nameof(bandCount));
        }

        if (frameCount < 1)
        {
            throw new ArgumentException("Il faut au moins une trame.", nameof(frameCount));
        }

        if (mel.Length < bandCount * frameCount)
        {
            throw new ArgumentException("Le mel est plus court que la grille annoncee.", nameof(mel));
        }

        double[] decibels = ToDecibels(mel, bandCount, frameCount);
        int lag = AnalysisSettings.OnsetLag;
        int lead = lag + (AnalysisSettings.Nfft / (2 * AnalysisSettings.HopLength));
        var flux = new double[bandCount * frameCount];
        for (int frame = lag; frame < frameCount; frame++)
        {
            // La lecture se fait a la trame courante ; l'ecriture est decalee de
            // `lead`, parce que la difference de la trame `i` ne peut decrire que
            // ce qui s'ecoule vers `i`. Confondre les deux indices fait glisser
            // toute l'enveloppe de trois trames, et avec elle tous les onsets.
            int current = frame * bandCount;
            int previous = (frame - lag) * bandCount;
            int targetFrame = frame - lag + lead;
            if (targetFrame < 0 || targetFrame >= frameCount)
            {
                continue;
            }

            int target = targetFrame * bandCount;

            for (int band = 0; band < bandCount; band++)
            {
                double rise = decibels[current + band] - decibels[previous + band];
                flux[target + band] = rise > 0.0 ? rise : 0.0;
            }
        }

        return new OnsetBands(flux, bandCount, frameCount, hopSeconds);
    }

    private static double[] ToDecibels(double[] mel, int bandCount, int frameCount)
    {
        var decibels = new double[bandCount * frameCount];
        double amin = AnalysisSettings.MinimumDecibels;
        double reference = 10.0 * Math.Log10(Math.Max(amin, 1.0));
        double peak = double.NegativeInfinity;
        for (int i = 0; i < decibels.Length; i++)
        {
            double value = 10.0 * Math.Log10(Math.Max(amin, mel[i])) - reference;
            decibels[i] = value;
            if (value > peak)
            {
                peak = value;
            }
        }

        double floor = peak - AnalysisSettings.TopDecibels;
        for (int i = 0; i < decibels.Length; i++)
        {
            if (decibels[i] < floor)
            {
                decibels[i] = floor;
            }
        }

        return decibels;
    }
}
