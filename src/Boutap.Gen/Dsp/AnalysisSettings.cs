// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Boutap.Gen.Dsp;

/// <summary>
/// Les parametres d'analyse, figes une fois pour toutes.
/// </summary>
/// <remarks>
/// Ces valeurs ne sont pas des preferences : ce sont celles du script
/// <c>tools/make-goldens.py</c>, qui a produit <c>tests/data/goldens</c> en
/// transcrivant les formules de librosa 0.10.2. Les changer sans regenerer les
/// references rendrait les tests absurdes : ils compareraient deux reglages
/// differents et echoueraient sans dire pourquoi.
/// <para>
/// Chaque constante porte la reference de la fonction librosa d'origine. C'est
/// ce qui permet de verifier le portage contre l'oracle plutot que contre une
/// opinion.
/// </para>
/// </remarks>
public static class AnalysisSettings
{
    /// <summary>Taille de la transformee de Fourier, en echantillons.</summary>
    public const int Nfft = 2048;

    /// <summary>Saut entre deux trames consecutive, en echantillons.</summary>
    public const int HopLength = 512;

    /// <summary>Nombre de bandes mel.</summary>
    public const int MelBands = 128;

    /// <summary>Nombre de classes de hauteur du chroma.</summary>
    public const int ChromaBins = 12;

    /// <summary>
    /// Desaccord en demi-tons applique a A4. Zero : on suppose le accorde
    /// standard. Voir <c>core.pitch.hz_to_octs</c>.
    /// </summary>
    public const double Tuning = 0.0;

    /// <summary>Exposant applique au spectre avant sommation. 2 = puissance.</summary>
    public const double Power = 2.0;

    /// <summary>Plancher du passage en decibels, pour ne pas diverger sur le silence.</summary>
    public const double MinimumDecibels = 1e-10;

    /// <summary>Etendue dynamique conservee, en decibels. <c>core.spectrum.power_to_db</c>.</summary>
    public const double TopDecibels = 80.0;

    /// <summary>
    /// Retard de la difference spectrale qui produit l'enveloppe d'onsets, en
    /// trames. <c>onset.onset_strength_multi(lag=1)</c>.
    /// </summary>
    public const int OnsetLag = 1;

    /// <summary>Nombre de valeurs de la fenetre de Hann. Toutes en sont utilisees.</summary>
    public const int WindowLength = Nfft;

    /// <summary>Nombre de coefficients bins d'une transformee reelle de taille <see cref="Nfft"/>.</summary>
    public const int BinCount = (Nfft / 2) + 1;

    /// <summary>Frequence d'etalonnage, en Hz.</summary>
    public const double A440 = 440.0;

    /// <summary>
    /// Nombre de trames d'un signal de <paramref name="sampleCount"/> echantillons.
    /// </summary>
    /// <remarks>
    /// Une tranche de la moitie de la fenetre est reflechee de chaque cote, donc
    /// la premiere et la derniere trame sont centrees sur le premier et le
    /// dernier echantillon. La formule est celle de <c>librosa.stft</c> :
    /// <c>1 + n // hop</c>.
    /// </remarks>
    /// <param name="sampleCount">Nombre d'echantillons du signal.</param>
    /// <returns>Le nombre de trames.</returns>
    public static int FrameCount(int sampleCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sampleCount);
        return 1 + (sampleCount / HopLength);
    }

    /// <summary>Instant de depart d'une trame, en secondes, pour l'analyseur d'onsets.</summary>
    /// <param name="frame">Indice de trame.</param>
    /// <returns>Le temps en secondes.</returns>
    public static double FrameSeconds(int frame) =>
        ((double)frame * HopLength) / 22050.0;
}
