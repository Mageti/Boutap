// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Boutap.Gen.Dsp;

namespace Boutap.Gen.Analysis;

/// <summary>
/// L'enveloppe d'onsets : une valeur par trame, qui monte quand l'energie
/// spectrale monte.
/// </summary>
/// <remarks>
/// <para>
/// L'ordre des operations n'est pas un detail. librosa calcule l'enveloppe sur
/// le mel <em>en decibels</em>, et pas en lineaire : c'est la perception
/// auditive qui compte, pas l'energie brute. Une enveloppe calculee sur des
/// puissances lineaires resterait collee au plancher pendant les montees, et le
/// seuil de detection ne vaudrait plus rien.
/// </para>
/// <para>
/// La sequence suit <c>onset.onset_strength_multi</c> : passage en decibels,
/// difference avec la trame precedente ecretee a zero, moyenne sur les bandes,
/// puis rebouchage du retard de bord. Elle est entierement portee par
/// <see cref="OnsetBands"/>, dont cette classe n'est que la moyenne sur les
/// bandes : deux implementations, deux jeux de tests, une seule verite.
/// </para>
/// </remarks>
public static class OnsetEnvelope
{
    /// <summary>Calcule l'enveloppe d'onsets d'un spectrogramme mel.</summary>
    /// <param name="mel">La puissance par bande mel, trame par trame.</param>
    /// <param name="bandCount">Le nombre de bandes mel.</param>
    /// <param name="frameCount">Le nombre de trames.</param>
    /// <returns>Une valeur par trame, les premieres valant zero.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Un nombre de bandes ou de trames est nul.</exception>
    /// <exception cref="ArgumentException">Le tableau ne contient pas trames x bandes valeurs.</exception>
    public static double[] Compute(ReadOnlySpan<double> mel, int bandCount, int frameCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(bandCount, 1);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameCount);
        if (mel.Length != bandCount * frameCount)
        {
            throw new ArgumentException(
                $"Le mel contient {mel.Length} valeurs, il en faut {bandCount * frameCount}.",
                nameof(mel));
        }

        double hopSeconds = (double)AnalysisSettings.HopLength;
        OnsetBands bands = OnsetBands.Compute(mel.ToArray(), bandCount, frameCount, hopSeconds);
        var envelope = new double[frameCount];
        for (int frame = 0; frame < frameCount; frame++)
        {
            envelope[frame] = bands.ColumnMean(frame);
        }

        return envelope;
    }
}
