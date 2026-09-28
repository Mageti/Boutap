// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Boutap.Gen.Dsp;

/// <summary>
/// La transformation d'un spectrogramme en mel et en chroma.
/// </summary>
/// <remarks>
/// Les deux sorties sont stockees trame par trame : c'est l'ordre de lecture de
/// l'analyseur d'onsets et du detecteur de tonique, et l'ordre d'ecriture du
/// fichier de test golden.
/// </remarks>
public static class Spectrograms
{
    /// <summary>Calcule le spectrogramme mel.</summary>
    /// <param name="spectrogram">Le module du spectrogramme court terme.</param>
    /// <param name="filterbank">Le banc mel.</param>
    /// <returns>
    /// La puissance par bande mel, lineaire. Le mel en decibels est une autre
    /// etape, faite uniquement la ou elle est necessaire.
    /// </returns>
    /// <exception cref="ArgumentNullException">Un parametre est nul.</exception>
    /// <exception cref="ArgumentException">Le banc ne couvre pas tous les bins.</exception>
    public static double[] Mel(MagnitudeSpectrogram spectrogram, MelFilterbank filterbank)
    {
        ArgumentNullException.ThrowIfNull(spectrogram);
        ArgumentNullException.ThrowIfNull(filterbank);
        if (filterbank.BinCount != spectrogram.BinCount)
        {
            throw new ArgumentException(
                $"Le banc couvre {filterbank.BinCount} bins, le spectrogramme en a {spectrogram.BinCount}.",
                nameof(filterbank));
        }

        int bands = filterbank.BandCount;
        int bins = spectrogram.BinCount;
        int frames = spectrogram.FrameCount;
        double[] mel = new double[frames * bands];
        for (int frame = 0; frame < frames; frame++)
        {
            for (int band = 0; band < bands; band++)
            {
                int weights = band * bins;
                double sum = 0.0;
                for (int bin = 0; bin < bins; bin++)
                {
                    double magnitude = spectrogram.Magnitude(frame, bin);
                    double weight = filterbank.Weight(band, bin);
                    if (weight != 0.0)
                    {
                        sum += (magnitude * magnitude) * weight;
                    }
                }

                mel[(frame * bands) + band] = sum;
            }
        }

        return mel;
    }

    /// <summary>Calcule le spectrogramme chromatique.</summary>
    /// <param name="spectrogram">Le module du spectrogramme court terme.</param>
    /// <param name="filterbank">Le banc chromatique.</param>
    /// <returns>La couleur par classe de hauteur, normalisee en norme 2.</returns>
    /// <exception cref="ArgumentNullException">Un parametre est nul.</exception>
    /// <exception cref="ArgumentException">Le banc ne couvre pas tous les bins.</exception>
    /// <remarks>
    /// La normalisation se fait sur les douze classes d'une meme trame, jamais
    /// sur les bandes d'une meme classe. L'inverse donne un chroma quasi
    /// constant : chaque tranche se normalise a 1, et le signal disparait au
    /// lieu de decrire la musique.
    /// </remarks>
    public static double[] Chroma(MagnitudeSpectrogram spectrogram, ChromaFilterbank filterbank)
    {
        ArgumentNullException.ThrowIfNull(spectrogram);
        ArgumentNullException.ThrowIfNull(filterbank);
        if (filterbank.BinCount != spectrogram.BinCount)
        {
            throw new ArgumentException(
                $"Le banc couvre {filterbank.BinCount} bins, le spectrogramme en a {spectrogram.BinCount}.",
                nameof(filterbank));
        }

        int classes = filterbank.ClassCount;
        int bins = spectrogram.BinCount;
        int frames = spectrogram.FrameCount;
        double[] chroma = new double[frames * classes];
        for (int frame = 0; frame < frames; frame++)
        {
            for (int pitchClass = 0; pitchClass < classes; pitchClass++)
            {
                int weights = pitchClass * bins;
                double sum = 0.0;
                for (int bin = 0; bin < bins; bin++)
                {
                    double weight = filterbank.Weight(pitchClass, bin);
                    if (weight != 0.0)
                    {
                        sum += spectrogram.Magnitude(frame, bin) * weight;
                    }
                }

                chroma[(frame * classes) + pitchClass] = sum;
            }

            double norm = 0.0;
            for (int pitchClass = 0; pitchClass < classes; pitchClass++)
            {
                norm += chroma[(frame * classes) + pitchClass] * chroma[(frame * classes) + pitchClass];
            }

            norm = Math.Sqrt(norm);
            if (norm < double.Epsilon)
            {
                continue;
            }

            for (int pitchClass = 0; pitchClass < classes; pitchClass++)
            {
                chroma[(frame * classes) + pitchClass] /= norm;
            }
        }

        return chroma;
    }
}
