// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Buffers;

namespace Boutap.Gen.Dsp;

/// <summary>
/// Transformee de Fourier rapide, iterative, en radix 2, sur un signal reel.
/// </summary>
/// <remarks>
/// <para>
/// Le projecteur d'origine utilise NumPy, qui calcule en double precision sur
/// des tableaux complexes. C'est cette sortie-la qui est comparee, donc c'est
/// elle qu'il faut reproduire : meme ordre d'operations, meme arrondi.
/// </para>
/// <para>
/// Une implementation maison plutot qu'un appel a une bibliotheque n'est pas un
/// caprice : le projet n'embarque aucune dependance NuGet (wiki: decision D1),
/// et un portage verifiable vaut mieux qu'une dependance qu'on ne peut pas
/// auditer.
/// </para>
/// </remarks>
public sealed class Fft
{
    private readonly double[] _cosine;
    private readonly double[] _sine;
    private readonly int[] _reversed;

    /// <summary>Construit un transformeur de taille <paramref name="size"/>.</summary>
    /// <param name="size">Taille de la transformee. Doit etre une puissance de deux.</param>
    /// <exception cref="ArgumentOutOfRangeException">La taille n'est pas une puissance de deux.</exception>
    public Fft(int size)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 2);
        if ((size & (size - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(size), size, "La taille de la transformee doit etre une puissance de deux.");
        }

        Size = size;
        _cosine = new double[size / 2];
        _sine = new double[size / 2];
        for (int index = 0; index < size / 2; index++)
        {
            double angle = 2.0 * Math.PI * index / size;
            _cosine[index] = Math.Cos(angle);
            _sine[index] = -Math.Sin(angle);
        }

        int bits = (int)Math.Log2(size);
        _reversed = new int[size];
        for (int index = 0; index < size; index++)
        {
            int value = 0;
            for (int bit = 0; bit < bits; bit++)
            {
                value = (value << 1) | ((index >> bit) & 1);
            }

            _reversed[index] = value;
        }
    }

    /// <summary>Taille de la transformee.</summary>
    public int Size { get; }

    /// <summary>
    /// Calcule le module du spectre d'un signal reel.
    /// </summary>
    /// <param name="samples">Le signal. Exactement <see cref="Size"/> echantillons.</param>
    /// <param name="magnitudes">
    /// Sortie : le module des <see cref="BinCount"/> premiers coefficients, la
    /// zone de Nyquist comprise.
    /// </param>
    /// <exception cref="ArgumentException">Le signal ou la sortie n'ont pas la bonne taille.</exception>
    public void Magnitudes(ReadOnlySpan<double> samples, Span<double> magnitudes)
    {
        int half = Size / 2;
        if (magnitudes.Length < half + 1)
        {
            throw new ArgumentException(
                $"Il faut au moins {half + 1} coefficents, il en est fourni {magnitudes.Length}.",
                nameof(magnitudes));
        }

        if (samples.Length != Size)
        {
            throw new ArgumentException(
                $"Le signal doit avoir {Size} echantillons, il en a {samples.Length}.",
                nameof(samples));
        }

        double[] real = ArrayPool<double>.Shared.Rent(Size);
        double[] imaginary = ArrayPool<double>.Shared.Rent(Size);
        try
        {
            for (int index = 0; index < Size; index++)
            {
                real[_reversed[index]] = samples[index];
                imaginary[_reversed[index]] = 0.0;
            }

            for (int length = 2; length <= Size; length <<= 1)
            {
                int step = Size / length;
                int halfLength = length / 2;
                for (int start = 0; start < Size; start += length)
                {
                    for (int offset = 0; offset < halfLength; offset++)
                    {
                        int twiddle = offset * step;
                        double wr = _cosine[twiddle];
                        double wi = _sine[twiddle];
                        int left = start + offset;
                        int right = left + halfLength;

                        double tr = (real[right] * wr) - (imaginary[right] * wi);
                        double ti = (real[right] * wi) + (imaginary[right] * wr);

                        real[right] = real[left] - tr;
                        imaginary[right] = imaginary[left] - ti;
                        real[left] += tr;
                        imaginary[left] += ti;
                    }
                }
            }

            for (int bin = 0; bin <= half; bin++)
            {
                double re = real[bin];
                double im = imaginary[bin];
                magnitudes[bin] = Math.Sqrt((re * re) + (im * im));
            }
        }
        finally
        {
            ArrayPool<double>.Shared.Return(real);
            ArrayPool<double>.Shared.Return(imaginary);
        }
    }
}
