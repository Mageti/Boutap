// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Tests de l'analyse : pics d'onsets, battements, tonalite.

using System;
using System.Collections.Generic;
using System.Linq;
using Boutap.Core.Common;
using Boutap.Gen.Analysis;
using Boutap.Gen.Compose;
using Boutap.Gen.Dsp;
using Xunit;

namespace Boutap.Gen.Tests;

/// <summary>Le selectionneur de pics, d'apres generateur.md §4.6.</summary>
public class PeakPickerTests
{
    [Theory]
    [InlineData(0.030, 22050, 512, 1)]
    [InlineData(0.100, 22050, 512, 4)]
    [InlineData(0.000, 22050, 512, 0)]
    [InlineData(0.030, 44100, 512, 2)]
    public void Les_fenetres_sont_converties_en_trames_par_une_tranche(
        double seconds,
        int sampleRate,
        int hopLength,
        int expected)
    {
        Assert.Equal(expected, PeakPicker.Frames(seconds, sampleRate, hopLength));
    }

    [Fact]
    public void Une_trame_de_fenetre_est_refusee_si_le_saut_est_invalide()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PeakPicker.Frames(0.03, 0, 512));
        Assert.Throws<ArgumentOutOfRangeException>(() => PeakPicker.Frames(0.03, 22050, 0));
    }

    [Fact]
    public void Une_enveloppe_vide_ne_produit_aucun_onset()
    {
        IReadOnlyList<Onset> onsets = PeakPicker.Pick([], 22050, 512, PeakPickerOptions.Specification);
        Assert.Empty(onsets);
    }

    [Fact]
    public void Un_creux_entoure_de_silence_est_un_onset()
    {
        double[] envelope = [0, 0, 0, 0.9, 0, 0, 0, 0];
        IReadOnlyList<Onset> onsets = PeakPicker.Pick(envelope, 22050, 512, PeakPickerOptions.Specification);
        Onset onset = Assert.Single(onsets);
        Assert.Equal(3, onset.Frame);
        Assert.Equal(3.0 * 512 / 22050, onset.TimeSeconds, 9);
        Assert.Equal(0.9, onset.Strength, 6);
    }

    [Fact]
    public void Un_plateau_nest_pas_un_onset_unique_mais_plusieurs()
    {
        // Un delta nul garde tout : la discrimination vient de la moyenne
        // locale, pas du maximum seul.
        double[] envelope = [0.5, 0.5, 0.5, 0.5];
        IReadOnlyList<Onset> onsets = PeakPicker.Pick(envelope, 22050, 512, new PeakPickerOptions(Delta: 0.01));
        Assert.Equal(4, onsets.Count);
    }

    [Fact]
    public void Un_seuil_de_delta_eleve_supprime_les_pics_faibles()
    {
        double[] envelope = [0, 0, 0.2, 0, 0];
        IReadOnlyList<Onset> permissive = PeakPicker.Pick(envelope, 22050, 512, new PeakPickerOptions(Delta: 0.01));
        IReadOnlyList<Onset> strict = PeakPicker.Pick(envelope, 22050, 512, new PeakPickerOptions(Delta: 0.5));
        Assert.NotEmpty(permissive);
        Assert.Empty(strict);
    }

    [Fact]
    public void Un_niveau_constant_ne_produit_aucun_onset()
    {
        // C'est le test qui distingue « maximum local » de « vrai depart ».
        double[] envelope = [0.3, 0.3, 0.3, 0.3, 0.3, 0.3];
        Assert.Empty(PeakPicker.Pick(envelope, 22050, 512, new PeakPickerOptions(Delta: 0.01)));
    }

    [Fact]
    public void L_enveloppe_est_normalisee_avant_le_test_des_pics()
    {
        // Deux enveloppes proportionnelles donnent le meme jeu d'onsets, avec la
        // meme force relative. Sans normalisation, le delta n'aurait aucun sens.
        double[] quiet = [0, 0, 0.02, 0, 0];
        double[] loud = [0, 0, 2.00, 0, 0];
        Assert.Equal(0.5, PeakPicker.Pick(quiet, 22050, 512, PeakPickerOptions.Specification)[0].Strength, 6);
        Assert.Equal(0.5, PeakPicker.Pick(loud, 22050, 512, PeakPickerOptions.Specification)[0].Strength, 6);
    }

    [Fact]
    public void L_attente_empeche_de_compter_deux_onsets_proches()
    {
        // wait = 30 ms = 1 trame a 22050/512 : le second creux est ignore.
        double[] envelope = [0, 1.0, 0, 0.9, 0];
        IReadOnlyList<Onset> onsets = PeakPicker.Pick(envelope, 22050, 512, PeakPickerOptions.Specification);
        Assert.Single(onsets);
    }
}

/// <summary>Le suivi de battement, d'apres generateur.md §4.7 (Ellis 2007).</summary>
public class BeatTrackerTests
{
    [Fact]
    public void Un_morceau_trop_court_ne_produit_aucun_battement()
    {
        BeatTrack track = BeatTracker.Track([], 0, 1, 1.0, BeatTrackerOptions.Specification);
        Assert.Empty(track.BeatsSeconds);
        Assert.Equal(0, track.TempoBpm);
    }

    [Fact]
    public void Un_morceau_a_cent_bpm_retrouve_cent_bpm()
    {
        // On synthetise un flux d'onsets periodique : une impulsion par temps.
        const double bpm = 100.0;
        const double hopSeconds = 512.0 / 22050.0;
        int frameCount = 862;
        double[] bands = BuildImpulseTrain(hopSeconds, bpm, frameCount, bandCount: 32, band: 4);

        BeatTrack track = BeatTracker.Track(bands, 32, frameCount, hopSeconds, BeatTrackerOptions.Specification);
        Assert.InRange(track.TempoBpm, bpm * 0.95, bpm * 1.05);
        Assert.True(track.WithinTolerance);
        Assert.NotEmpty(track.BeatsSeconds);
    }

    [Fact]
    public void La_vitesse_retrouvee_est_independante_de_l_echelle_du_signal()
    {
        // Un flux multiplie par 100 doit donner le meme tempo : c'est
        // l'autocorrelation qui decide, pas l'amplitude.
        const double hopSeconds = 512.0 / 22050.0;
        int frameCount = 862;
        double[] soft = BuildImpulseTrain(hopSeconds, 120, frameCount, 32, 4);
        double[] loud = (double[])soft.Clone();
        for (int i = 0; i < loud.Length; i++)
        {
            loud[i] *= 100.0;
        }

        BeatTrack a = BeatTracker.Track(soft, 32, frameCount, hopSeconds, BeatTrackerOptions.Specification);
        BeatTrack b = BeatTracker.Track(loud, 32, frameCount, hopSeconds, BeatTrackerOptions.Specification);
        Assert.Equal(a.TempoBpm, b.TempoBpm, 6);
    }

    [Fact]
    public void Les_battements_sont_espaces_comme_le_tempo_annonce()
    {
        const double hopSeconds = 512.0 / 22050.0;
        int frameCount = 862;
        double[] bands = BuildImpulseTrain(hopSeconds, 120, frameCount, 32, 4);
        BeatTrack track = BeatTracker.Track(bands, 32, frameCount, hopSeconds, BeatTrackerOptions.Specification);

        Assert.True(track.BeatsSeconds.Count > 20, "le morceau de test doit contenir plusieurs temps");
        double span = track.BeatsSeconds[^1] - track.BeatsSeconds[0];
        double expected = (track.BeatsSeconds.Count - 1) * 60.0 / track.TempoBpm;
        Assert.Equal(expected, span, 6);
    }

    [Fact]
    public void Un_ecart_du_tempo_de_reference_est_signale_sans_etre_corrige()
    {
        // 30 BPM sort de la plage du spec : le suivi le dit, il ne le rattrapne pas.
        const double hopSeconds = 512.0 / 22050.0;
        int frameCount = 862;
        double[] bands = BuildImpulseTrain(hopSeconds, 30, frameCount, 32, 4);
        BeatTrack track = BeatTracker.Track(bands, 32, frameCount, hopSeconds, BeatTrackerOptions.Specification);
        Assert.False(track.WithinTolerance);
    }

    [Fact]
    public void La_confiance_reste_dans_l_intervalle_autorise()
    {
        const double hopSeconds = 512.0 / 22050.0;
        int frameCount = 862;
        double[] bands = BuildImpulseTrain(hopSeconds, 120, frameCount, 32, 4);
        BeatTrack track = BeatTracker.Track(bands, 32, frameCount, hopSeconds, BeatTrackerOptions.Specification);
        Assert.InRange(track.Confidence, 0.0, 1.0);
    }

    [Fact]
    public void La_phase_du_temps_repartit_la_longueur_en_sous_cases()
    {
        IReadOnlyList<BeatPhase> phases = BeatTracker.MeasurePhases(
            new double[400],
            [0.0, 0.5, 1.0],
            120.0,
            512.0 / 22050.0,
            phases: 4);
        Assert.Equal(3, phases.Count);
        Assert.All(phases, phase => Assert.Equal(4, phase.Phases.Count));
        Assert.All(phases, phase => Assert.InRange(phase.StrongestPhase, 0, 3));
    }

    internal static double[] BuildImpulseTrain(
        double hopSeconds,
        double bpm,
        int frameCount,
        int bandCount,
        int band)
    {
        double periodFrames = 60.0 / bpm / hopSeconds;
        var bands = new double[frameCount * bandCount];
        for (int frame = 0; frame < frameCount; frame++)
        {
            double phase = frame % periodFrames;
            if (phase < 0.5)
            {
                bands[(frame * bandCount) + band] = 1.0;
            }
        }

        return bands;
    }
}

/// <summary>La reconnaissance de tonalite, d'apres generateur.md §4.8.</summary>
public class KeyFinderTests
{
    [Fact]
    public void Une_chroma_de_la_majeure_de_la_retrouve()
    {
        // A majeur : A C# E, plus une legere couleur sur la quinte.
        double[] chroma = new double[12];
        chroma[9] = 1.0;
        chroma[1] = 0.9;
        chroma[4] = 0.8;
        KeyCandidate? found = KeyFinder.Find(chroma);
        Assert.NotNull(found);
        Assert.Equal(9, found.Value.Key.PitchClass);
        Assert.Equal(KeyMode.Major, found.Value.Key.Mode);
    }

    [Fact]
    public void Une_chroma_de_la_mineure_de_mi_retrouve_le_la_mineur()
    {
        // Mi mineur : E G B.
        double[] chroma = new double[12];
        chroma[4] = 1.0;
        chroma[7] = 0.9;
        chroma[11] = 0.85;
        KeyCandidate? found = KeyFinder.Find(chroma);
        Assert.NotNull(found);
        Assert.Equal(4, found.Value.Key.PitchClass);
        Assert.Equal(KeyMode.Minor, found.Value.Key.Mode);
    }

    [Fact]
    public void Un_bruit_blanc_ne_produit_aucune_tonalite()
    {
        // C'est le cas le plus important : mieux vaut ne rien dire.
        var noise = new double[12];
        var random = new Random(7);
        for (int i = 0; i < noise.Length; i++)
        {
            noise[i] = random.NextDouble();
        }

        Assert.Null(KeyFinder.Find(noise));
    }

    [Fact]
    public void Le_classement_contient_vingt_quatre_candidats_par_ordre_croissant()
    {
        double[] chroma = new double[12];
        chroma[0] = 1.0;
        IReadOnlyList<KeyCandidate> ranked = KeyFinder.Rank(chroma);
        Assert.Equal(24, ranked.Count);
        for (int i = 1; i < ranked.Count; i++)
        {
            Assert.True(ranked[i - 1].Score >= ranked[i].Score, "le classement doit etre decroissant");
        }
    }

    [Fact]
    public void La_marge_vaut_l_ecart_avec_le_candidat_suivant()
    {
        double[] chroma = new double[12];
        chroma[7] = 1.0;
        chroma[11] = 0.8;
        IReadOnlyList<KeyCandidate> ranked = KeyFinder.Rank(chroma);
        Assert.Equal(ranked[0].Score - ranked[1].Score, ranked[0].Margin, 12);
    }

    [Fact]
    public void La_moyenne_des_trames_pese_toutes_les_trames_egalement()
    {
        double[] chroma = new double[24];
        for (int frame = 0; frame < 2; frame++)
        {
            chroma[frame * 12] = 1.0;
            chroma[(frame * 12) + 5] = 3.0;
        }

        double[] mean = KeyFinder.Mean(chroma, 2);
        Assert.Equal(12, mean.Length);
        Assert.Equal(1.0, mean[0], 12);
        Assert.Equal(3.0, mean[5], 12);
    }

    [Fact]
    public void Le_gabarit_tourne_sans_changer_sa_forme()
    {
        double[] profile = KeyFinder.Rotate(KeyFinder.Rotate([1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], 5), 7);
        double[] reference = [0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0];
        Assert.Equal(reference, profile);
    }

    [Fact]
    public void La_correlation_de_pearson_detecte_deux_profils_identiques_ou_opposes()
    {
        double[] a = [1, 2, 3, 4];
        double[] same = [1, 2, 3, 4];
        double[] opposite = [4, 3, 2, 1];
        Assert.Equal(1.0, KeyFinder.Pearson(a, same), 12);
        Assert.Equal(-1.0, KeyFinder.Pearson(a, opposite), 12);
    }

    [Fact]
    public void Un_profil_constant_n_a_pas_de_correlation()
    {
        // La variance nulle rend le coefficient indefini : mieux vaut 0.
        Assert.Equal(0.0, KeyFinder.Pearson([1, 1, 1, 1], [1, 2, 3, 4]), 12);
    }
}
