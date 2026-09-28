// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Boutap.Gen.Dsp;

/// <summary>
/// Un banc chromatique : douze bosses gaussiennes, une par classe de hauteur.
/// </summary>
/// <remarks>
/// <para>
/// Le chroma repond a « de quelle couleur est ce son », pas « a quelle
/// frequence ». Les douze classes sont donc des bosses en position tonale, et
/// non des bandes de frequence : une meme note jouee trois octaves plus haut
/// tombe dans la meme colonne.
/// </para>
/// <para>
/// Trois etapes successives, dans cet ordre exact. Les permuter change le
/// resultat de facon insaisissable, c'est pourquoi le portage les suit a la
/// lettre plutot que de les « simplifier ».
/// </para>
/// <list type="number">
/// <item>Les bosses sont construites puis divisees par leur norme, colonne par
/// colonne, pour qu'un bin presque sourd ne soit pas amplifie enorme.</item>
/// <item>Une fenetre de dominance en octaves eteint les extrmes, sans quoi les
/// bins vides du numerateur et du denominateur se compensent et le signal
/// disparait.</item>
/// <item>Les lignes sont decalees de trois tons pour que la premiere colonne
/// soit un do et non un la.</item>
/// </list>
/// </remarks>
public sealed class ChromaFilterbank
{
    private readonly double[] _weights;

    private ChromaFilterbank(double[] weights, int binCount, int classCount)
    {
        _weights = weights;
        BinCount = binCount;
        ClassCount = classCount;
    }

    /// <summary>Nombre de classes de hauteur, douze par convention.</summary>
    public int ClassCount { get; }

    /// <summary>Nombre de bins de la transformee que le banc couvre.</summary>
    public int BinCount { get; }

    /// <summary>Le poids d'une classe pour un bin donne.</summary>
    /// <param name="pitchClass">La classe de hauteur, de zero a onze.</param>
    /// <param name="bin">Le bin.</param>
    /// <returns>Le poids.</returns>
    public double Weight(int pitchClass, int bin) => _weights[(pitchClass * BinCount) + bin];

    /// <summary>Construit un banc chromatique.</summary>
    /// <param name="sampleRate">La frequence d'echantillonnage, en Hz.</param>
    /// <param name="fftSize">La taille de la transformee.</param>
    /// <param name="classCount">Le nombre de classes.</param>
    /// <param name="tuning">Le desaccord applique a A4, en demi-tons.</param>
    /// <returns>Le banc.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Un parametre est hors bornes.</exception>
    public static ChromaFilterbank Create(double sampleRate, int fftSize, int classCount, double tuning)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(classCount, 1);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fftSize);
        if ((fftSize & (fftSize - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(fftSize), fftSize, "La taille de la transformee doit etre une puissance de deux.");
        }

        int binCount = (fftSize / 2) + 1;
        double reference = AnalysisSettings.A440 * Math.Pow(2.0, tuning / classCount);
        double[] bins = Positions(sampleRate, fftSize, classCount, reference, binCount + 1);
        double[] weights = new double[classCount * binCount];
        int shift = -3 * (classCount / 12);
        double half = Math.Round(classCount / 2.0, MidpointRounding.AwayFromZero);

        // Etape 1 : les bosses brutes, puis leur norme par colonne.
        for (int bin = 0; bin < binCount; bin++)
        {
            double width = Math.Max(bins[bin + 1] - bins[bin], 1.0);
            double sum = 0.0;
            for (int pitchClass = 0; pitchClass < classCount; pitchClass++)
            {
                double delta = Remainder(bins[bin] - pitchClass + half + (10.0 * classCount), classCount) - half;
                double value = Math.Exp(-0.5 * Math.Pow((2.0 * delta) / width, 2.0));
                weights[(Shifted(pitchClass, shift, classCount) * binCount) + bin] = value;
                sum += value * value;
            }

            // Etape 2 : la fenetre de dominance en octaves, appliquee apres la
            // normalisation, comme dans librosa.
            double dominant = Math.Exp(-0.5 * Math.Pow(((bins[bin] / classCount) - 5.0) / 2.0, 2.0));
            double divisor = Math.Sqrt(sum) < double.Epsilon ? 1.0 : Math.Sqrt(sum);
            for (int pitchClass = 0; pitchClass < classCount; pitchClass++)
            {
                int row = Shifted(pitchClass, shift, classCount);
                weights[(row * binCount) + bin] = (weights[(row * binCount) + bin] / divisor) * dominant;
            }
        }

        return new ChromaFilterbank(weights, binCount, classCount);
    }

    /// <summary>
    /// Les positions tonales des colonnes, en demi-tons depuis le do le plus bas.
    /// </summary>
    /// <param name="sampleRate">La frequence d'echantillonnage, en Hz.</param>
    /// <param name="fftSize">La taille de la transformee.</param>
    /// <param name="classCount">Le nombre de classes.</param>
    /// <param name="reference">La frequence de reference A4 desaccordée, en Hz.</param>
    /// <param name="count">Le nombre de positions voulues.</param>
    /// <returns>Les positions.</returns>
    /// <remarks>
    /// La premiere colonne est une fiction : il n'y a pas de bin a frequence
    /// nulle dans une transformee reelle, on lui invente donc une position une
    /// octave et demie plus basse que la deuxieme. Sans elle, la premiere
    /// colonne peckerait au hasard plutot que de decrire le do grave.
    /// </remarks>
    private static double[] Positions(
        double sampleRate, int fftSize, int classCount, double reference, int count)
    {
        double[] bins = new double[count];
        for (int index = 1; index < count; index++)
        {
            double frequency = (sampleRate * index) / fftSize;
            bins[index] = classCount * Math.Log2(frequency / (reference / 16.0));
        }

        bins[0] = bins[1] - (1.5 * classCount);
        return bins;
    }

    /// <summary>
    /// Le reste d'une division euclidienne, toujours positif pour un diviseur
    /// positif, comme <c>numpy.remainder</c>.
    /// </summary>
    /// <param name="value">Le numerateur.</param>
    /// <param name="divisor">Le diviseur, strictement positif.</param>
    /// <returns>Le reste, dans l'intervalle <c>[0, divisor]</c>.</returns>
    internal static double Remainder(double value, double divisor)
    {
        double rest = value % divisor;
        return rest < 0.0 ? rest + divisor : rest;
    }

    private static int Shifted(int pitchClass, int shift, int count)
    {
        int moved = pitchClass + shift;
        while (moved < 0)
        {
            moved += count;
        }

        return moved % count;
    }
}
