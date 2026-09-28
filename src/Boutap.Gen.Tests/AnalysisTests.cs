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

        // La force est une amplitude normalisee, pas une mesure absolue : le
        // plus fort depart d'une enveloppe vaut 1 par construction. C'est ce
        // qui rend le delta comparable d'un morceau a l'autre.
        Assert.Equal(1.0, onset.Strength, 9);
    }

    [Fact]
    public void Un_plateau_nest_pas_un_onset_unique_mais_plusieurs()
    {
        // Un palier tenu n'a qu'un seul maximum au sens de l'enveloppe, mais
        // chacune de ses trames est un maximum local des laisons d'un frame.
        // Ce qui les confond, c'est l'attente, pas le delta : a une trame, le
        // palier ne donne qu'un onset, a zero il en donne un par trame.
        //
        // Le palier doit rester au-dessus du minimum de l'enveloppe : la
        // normalisation retranche ce minimum, et un palier constant tombe donc
        // a zero (c'est le cas limite de Un_niveau_constant_ne_produit_aucun_onset).
        double[] envelope = [0.2, 0.5, 0.5, 0.5];
        IReadOnlyList<Onset> onsets = PeakPicker.Pick(
            envelope,
            22050,
            512,
            new PeakPickerOptions(Delta: 0.01, WaitSeconds: 0.0));
        Assert.Equal(3, onsets.Count);
        Assert.Equal("1,2,3", string.Join(",", onsets.Select(onset => onset.Frame)));
    }

    [Fact]
    public void Un_seuil_de_delta_eleve_supprime_les_pics_faibles()
    {
        // Le delta est un ecart relatif a la moyenne locale, pas un seuil
        // absolu : allonger l'enveloppe ne le rend donc pas plus severe, puisque
        // la moyenne monte avec le pic. Pour illustrer la discrimination, il
        // faut deux pics d'amplitude differente.
        double[] envelope = [0, 0, 1.0, 0, 0, 0, 0.4, 0, 0];
        IReadOnlyList<Onset> permissive = PeakPicker.Pick(envelope, 22050, 512, new PeakPickerOptions(Delta: 0.01));
        IReadOnlyList<Onset> strict = PeakPicker.Pick(envelope, 22050, 512, new PeakPickerOptions(Delta: 0.5));
        Assert.Equal("2,6", string.Join(",", permissive.Select(onset => onset.Frame)));
        Assert.Equal("2", string.Join(",", strict.Select(onset => onset.Frame)));
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
        // Deux enveloppes proportionnelles donnent le meme jeu d'onsets, avec les
        // memes forces relatives. Sans normalisation, le delta n'aurait aucun
        // sens : un passage discret ne franchirait jamais le seuil qu'un passage
        // fort franchit, et les passages discrets disparaissent du graphique.
        double[] quiet = [0, 0, 2.0, 0, 0, 0, 0.8, 0, 0];
        double[] loud = (double[])quiet.Clone();
        for (int i = 0; i < loud.Length; i++)
        {
            loud[i] *= 100.0;
        }

        IReadOnlyList<Onset> a = PeakPicker.Pick(quiet, 22050, 512, PeakPickerOptions.Specification);
        IReadOnlyList<Onset> b = PeakPicker.Pick(loud, 22050, 512, PeakPickerOptions.Specification);
        string quietFrames = string.Join(",", a.Select(onset => onset.Frame));
        Assert.Equal("2,6", quietFrames);
        Assert.Equal(quietFrames, string.Join(",", b.Select(onset => onset.Frame)));
        Assert.Equal(1.0, a[0].Strength, 9);
        Assert.Equal(0.4, a[1].Strength, 9);
    }

    [Fact]
    public void L_attente_empeche_de_compter_deux_onsets_proches()
    {
        // wait = 30 ms = 1 trame a 22050/512. Les deux creux sont a une seule
        // trame d'ecart, donc le second tombe dans l'attente du premier : un
        // roulement de hi-hat ne doit pas compter double.
        double[] envelope = [0, 1.0, 0.9, 0, 0];
        IReadOnlyList<Onset> onsets = PeakPicker.Pick(envelope, 22050, 512, PeakPickerOptions.Specification);
        Onset onset = Assert.Single(onsets);
        Assert.Equal(1, onset.Frame);
        Assert.Equal(1.0, onset.Strength, 9);
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
        Assert.NotEmpty(track.BeatsSeconds);

        // 100 BPM est hors de la bande de +/- 10 % autour de l'apriori de
        // 120 : c'est la mesure qui compte ici, et WithinTolerance est une autre
        // question, posee par Un_ecart_du_tempo_de_reference_est_signale...
        Assert.False(track.WithinTolerance);
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
        // 200 BPM est dans la plage de recherche [40, 200] mais tres loin de
        // l'apriori de 120 BPM. Le suivi doit signaler l'ecart au lieu de
        // recaler la mesure sur l'apriori : une fausse certitude arrangee est
        // pire qu'un tempo annonce faux.
        const double hopSeconds = 512.0 / 22050.0;
        int frameCount = 862;
        double[] bands = BuildImpulseTrain(hopSeconds, 200, frameCount, 32, 4);
        BeatTrack track = BeatTracker.Track(bands, 32, frameCount, hopSeconds, BeatTrackerOptions.Specification);
        Assert.False(track.WithinTolerance);
        Assert.True(track.TempoBpm > 150.0, "la mesure reste proche de 200, elle n'est pas recalee sur 120");
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
    public void Un_chroma_sans_ecart_ne_produit_aucune_tonalite()
    {
        // C'est le cas le plus important : mieux vaut ne rien dire. Un chroma
        // plat n'a aucune variance, donc aucune correlation possible, et le
        // meilleur score vaut zero. Un bruit blanc lui aussi n'a pas de tonique,
        // mais il en a une variance : le classement produit alors un gagnant
        // arbitraire. Le seuil de refus est donc porte par le score, pas par
        // une decision binaire sur l'egalite des douze valeurs.
        Assert.Null(KeyFinder.Find(new double[12]));
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
        // La rotation est cumulative et circulaire : sept de plus que cinq
        // fait douze, donc un tour complet, et le gabarit revient a sa place.
        double[] original = [1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
        Assert.Equal(5, Array.IndexOf(KeyFinder.Rotate(original, 5), 1.0));
        Assert.Equal(original, KeyFinder.Rotate(KeyFinder.Rotate(original, 5), 7));
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
