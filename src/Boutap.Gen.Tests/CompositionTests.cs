// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Tests du cout de passage d'une note a l'autre, de l'humanisation
// deterministe, du nettoyage, et de la chaine complete jusqu'au pack.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Boutap.Core.Pack;
using Boutap.Gen.Compose;
using Xunit;

namespace Boutap.Gen.Tests;

/// <summary>Le cout de passage d'une note a l'autre, et ce qu'un joueur atteint.</summary>
public class PlayerSimulatorTests
{
    private const double Tolerance = 1e-9;

    private static string Instants(IReadOnlyList<Note> notes) =>
        string.Join(",", notes.Select(note => note.Time.ToString("0.000", CultureInfo.InvariantCulture)));

    [Fact]
    public void Une_touche_voisine_cout_une_cible_de_deplacement()
    {
        Assert.Equal(0.055, PlayerSimulator.Transition(Note.Tap(0.0, KeyBinding.Grid(0)), Note.Tap(0.1, KeyBinding.Grid(1)), PlayerSimulatorOptions.Adult), 9);
        Assert.Equal(0.055, PlayerSimulator.Transition(Note.Tap(0.0, KeyBinding.Grid(0)), Note.Tap(0.1, KeyBinding.Grid(3)), PlayerSimulatorOptions.Adult), 9);
    }

    [Fact]
    public void Rejouer_la_meme_touche_cout_une_cible()
    {
        double cost = PlayerSimulator.Transition(
            Note.Tap(0.0, KeyBinding.Grid(4)),
            Note.Tap(0.1, KeyBinding.Grid(4)),
            PlayerSimulatorOptions.Adult);

        Assert.Equal(0.055, cost, 9);
    }

    [Fact]
    public void Changer_de_main_coute_plus_que_se_deplacer()
    {
        double cost = PlayerSimulator.Transition(
            Note.Tap(0.0, KeyBinding.Grid(0)),
            Note.Tap(0.1, KeyBinding.Grid(6)),
            PlayerSimulatorOptions.Adult);

        // Deux touches franchies, et le passage d'une main a l'autre se paie.
        Assert.Equal(0.270, cost, 9);
        Assert.True(
            cost > PlayerSimulator.Transition(Note.Tap(0.0, KeyBinding.Grid(0)), Note.Tap(0.1, KeyBinding.Grid(1)), PlayerSimulatorOptions.Adult),
            "Un changement de main doit couter plus cher qu'un deplacement.");
    }

    [Fact]
    public void Un_volant_nest_pas_sur_la_grille_et_coute_un_changement_de_main()
    {
        Assert.Equal(
            PlayerSimulatorOptions.Adult.OtherHandBaseSeconds,
            PlayerSimulator.Transition(Note.Tap(0.0, KeyBinding.Grid(0)), Note.Tap(0.1, KeyBinding.WheelLeft), PlayerSimulatorOptions.Adult),
            9);
    }

    [Fact]
    public void Une_touche_deja_enfoncee_ne_coute_rien()
    {
        // Le premier tenu maintient la touche de 0 a 1 seconde ; le second
        // n'exige pas de la relâcher avant de l'enfoncer de nouveau.
        Assert.Equal(
            0.0,
            PlayerSimulator.Transition(
                Note.Hold(0.0, KeyBinding.Grid(0), 1.0),
                Note.Hold(0.5, KeyBinding.Grid(0), 1.0),
                PlayerSimulatorOptions.Adult),
            9);
    }

    [Fact]
    public void Une_touche_relachee_puis_reenfoncee_redemande_un_deplacement()
    {
        // Le second tenu commence apres la fin du premier : la main a eu le
        // temps de revenir, et la touche n'est plus enfoncee.
        Assert.Equal(
            0.055,
            PlayerSimulator.Transition(
                Note.Hold(0.0, KeyBinding.Grid(0), 1.0),
                Note.Hold(1.5, KeyBinding.Grid(0), 1.0),
                PlayerSimulatorOptions.Adult),
            9);
    }

    [Fact]
    public void Une_pressee_simple_ne_compte_pas_comme_une_touche_enfoncee()
    {
        Assert.Equal(
            0.055,
            PlayerSimulator.Transition(
                Note.Tap(0.0, KeyBinding.Grid(0)),
                Note.Hold(0.5, KeyBinding.Grid(0), 1.0),
                PlayerSimulatorOptions.Adult),
            9);
    }

    [Fact]
    public void Seul_le_troisieme_tiers_de_la_grille_est_joue_a_droite()
    {
        Assert.Equal(
            "0,0,0,0,0,0,1,1,1",
            string.Join(",", Enumerable.Range(0, 9).Select(index => PlayerSimulator.Hand(index))));
    }

    [Fact]
    public void Le_niveau_berceau_demande_plus_de_temps_de_reaction()
    {
        Assert.Equal(0.350, PlayerSimulatorOptions.ForLevel(ChartLevel.Berceau).ReactionSeconds, 9);
        Assert.Equal(0.220, PlayerSimulatorOptions.ForLevel(ChartLevel.Ronde).ReactionSeconds, 9);
        Assert.Equal(0.220, PlayerSimulatorOptions.ForLevel(ChartLevel.Cascade).ReactionSeconds, 9);
        Assert.True(
            PlayerSimulatorOptions.Child.ReactionSeconds > PlayerSimulatorOptions.Adult.ReactionSeconds,
            "Le niveau berceau est celui des enfants, sa reaction est plus lente.");
    }

    [Fact]
    public void La_premiere_note_est_toujours_atteignable()
    {
        // Elle est la seule note qu'aucune autre ne precede : elle n'a rien a
        // atteindre, et le joueur n'a pas encore eu le temps de preparer sa main.
        SimulationReport report = PlayerSimulator.Simulate(
            [Note.Tap(0.0, KeyBinding.Grid(0))],
            ChartLevel.Ronde);

        Assert.Single(report.Reachable);
        Assert.Empty(report.Unreachable);
    }

    [Fact]
    public void Une_note_trop_rapprochee_est_signalee_et_non_deplacee()
    {
        SimulationReport report = PlayerSimulator.Simulate(
            [Note.Tap(0.000, KeyBinding.Grid(2)), Note.Tap(0.050, KeyBinding.Grid(2))],
            ChartLevel.Ronde);

        Assert.Equal("0.000", Instants(report.Reachable));
        UnreachableNote only = Assert.Single(report.Unreachable);
        Assert.Equal(1, only.NoteIndex);
        Assert.Equal(0.050, only.TimeSeconds, 9);
        Assert.Equal(0.055, only.RequiredSeconds, 9);
        Assert.Equal(0.050, only.AvailableSeconds, 9);

        // Le simulateur supprime, il ne deplace pas : le joueur entendrait le
        // morceau et verrait un motif deplace.
        Assert.Equal(0.000, report.Reachable[0].Time, 9);
    }

    [Fact]
    public void Une_note_inatteignable_ne_devient_pas_la_reference_suivante()
    {
        // Les trois notes se suivent sur la meme touche. Si la deuxieme, trop
        // rapprochee, devenait la reference, la troisieme serait elle aussi
        // declaree inatteignable alors que 0.100 suffisent depuis la premiere.
        SimulationReport report = PlayerSimulator.Simulate(
            [
                Note.Tap(0.000, KeyBinding.Grid(2)),
                Note.Tap(0.050, KeyBinding.Grid(2)),
                Note.Tap(0.100, KeyBinding.Grid(2)),
            ],
            ChartLevel.Ronde);

        Assert.Equal("0.000,0.100", Instants(report.Reachable));
        Assert.Equal(1, Assert.Single(report.Unreachable).NoteIndex);
    }

    [Theory]
    [InlineData(0.250, false)]
    [InlineData(0.300, true)]
    public void Un_ecart_suffisant_ne_suffit_pas_sans_le_temps_de_reaction(double time, bool reachable)
    {
        // Au-dela du temps de reaction, le joueur traite chaque note comme le
        // debut d'une phrase et doit donc relire la partition avant de jouer.
        // Un ecart de 250 ms ne laisse que 30 ms pour un deplacement de 55 ms.
        SimulationReport report = PlayerSimulator.Simulate(
            [Note.Tap(0.0, KeyBinding.Grid(0)), Note.Tap(time, KeyBinding.Grid(1))],
            ChartLevel.Ronde);

        Assert.Equal(reachable, report.Unreachable.Count == 0);
    }

    [Fact]
    public void Le_niveau_berceau_ecarte_ce_que_l_adulte_tient_a_la_limite()
    {
        // Contre-intuitif, et c'est la consequence directe du modele : le temps
        // de reaction n'est paye que si l'ecart depasse le seuil de phrase.
        // Ce seuil vaut 350 ms en berceau contre 220 ms ailleurs : a 300 ms,
        // l'adulte paie sa reaction et l'enfant non, et l'enfant passe.
        // A 400 ms, les deux paient, mais l'adulte a 125 ms de marge et
        // l'enfant 45 ms pour un deplacement qui en coute 55.
        Note[] notes = [Note.Tap(0.0, KeyBinding.Grid(0)), Note.Tap(0.400, KeyBinding.Grid(1))];

        Assert.Empty(PlayerSimulator.Simulate(notes, ChartLevel.Ronde).Unreachable);
        Assert.Single(PlayerSimulator.Simulate(notes, ChartLevel.Berceau).Unreachable);
    }
}

/// <summary>L'introduction d'une variation humaine, et le nettoyage qui la suit.</summary>
public class HumanizerTests
{
    private static readonly string Fingerprint = new('a', 64);
    private static readonly string OtherFingerprint = new('b', 64);

    private static Note[] Line(int count, int stepMilliseconds = 500) =>
        [.. Enumerable.Range(0, count).Select(index => Note.Tap(index * stepMilliseconds / 1000.0, KeyBinding.Grid(index % 9)))];

    private static string Instants(IReadOnlyList<Note> notes) =>
        string.Join(",", notes.Select(note => note.Time.ToString("0.000000", CultureInfo.InvariantCulture)));

    [Fact]
    public void Une_liste_vide_reste_vide()
    {
        Assert.Empty(Humanizer.Humanize([], Fingerprint, "0.1.0", ChartLevel.Ronde, 120.0));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    public void Un_tempo_non_positif_est_refuse(double tempo)
    {
        // Sans tempo, une sous-case n'a pas de duree : humaniser reviendrait a
        // choisir un nombre au hasard, ce qui serait une variante du joueur.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Humanizer.Humanize(Line(4), Fingerprint, "0.1.0", ChartLevel.Ronde, tempo));
    }

    [Fact]
    public void Le_jitter_ressemble_a_une_sous_case()
    {
        // A cent vingt temps par minute, un temps dure 500 ms et une sous-case
        // 31,25 ms. Le jitter etant d'un tiers de temps, il vaut un peu plus
        // d'une demi-sous-case, et la syncope en ajoute une entiere.
        double subCase = (60.0 / 120.0) / Humanizer.Subdivisions;
        double amplitude = 0.35 * (60.0 / 120.0) / Humanizer.Subdivisions;
        Note[] source = Line(32);
        IReadOnlyList<Note> result = Humanizer.Humanize(source, Fingerprint, "0.1.0", ChartLevel.Ronde, 120.0);

        Assert.Equal(source.Length, result.Count);
        for (int index = 0; index < source.Length; index++)
        {
            double shift = result[index].Time - source[index].Time;
            Assert.InRange(shift, -amplitude / 2, (amplitude / 2) + subCase);
        }
    }

    [Fact]
    public void La_syncope_avance_mais_ne_recule_pas()
    {
        // Une note repoussee devant la suivante disparaitrait derriere elle.
        double amplitude = 0.35 * (60.0 / 120.0) / Humanizer.Subdivisions;
        Note[] source = Line(64);
        IReadOnlyList<Note> result = Humanizer.Humanize(source, Fingerprint, "0.1.0", ChartLevel.Ronde, 120.0);

        for (int index = 0; index < source.Length; index++)
        {
            Assert.True(
                result[index].Time - source[index].Time >= -(amplitude / 2),
                $"La note {index} a recule alors qu'une syncope ne recule jamais.");
        }
    }

    [Fact]
    public void Un_jitter_nul_laisse_les_instants_intacts()
    {
        Note[] source = Line(16);
        HumanizerOptions silent = new(JitterBeats: 0.0, SyncopeProbability: 0.0);

        IReadOnlyList<Note> result = Humanizer.Humanize(source, Fingerprint, "0.1.0", ChartLevel.Ronde, 120.0, silent);

        for (int index = 0; index < source.Length; index++)
        {
            Assert.Equal(source[index].Time, result[index].Time, 12);
        }
    }

    [Fact]
    public void Le_meme_entree_donne_la_meme_sortie()
    {
        Note[] source = Line(24);

        string first = Instants(Humanizer.Humanize(source, Fingerprint, "0.1.0", ChartLevel.Ronde, 120.0));
        string second = Instants(Humanizer.Humanize(source, Fingerprint, "0.1.0", ChartLevel.Ronde, 120.0));

        // Deux generations du meme morceau doivent produire le meme pack, octet
        // pour octet. Tout ce qui n'est pas tire de la graine casse cela.
        Assert.Equal(first, second);
    }

    [Fact]
    public void Une_autre_empreinte_donne_un_autre_jitter()
    {
        Note[] source = Line(24);

        string first = Instants(Humanizer.Humanize(source, Fingerprint, "0.1.0", ChartLevel.Ronde, 120.0));
        string other = Instants(Humanizer.Humanize(source, OtherFingerprint, "0.1.0", ChartLevel.Ronde, 120.0));

        Assert.NotEqual(first, other);
    }

    [Fact]
    public void Le_niveau_participe_a_la_graine()
    {
        // Deux niveaux d'un meme morceau ne doivent pas se ressembler.
        Note[] source = Line(24);

        string cradle = Instants(Humanizer.Humanize(source, Fingerprint, "0.1.0", ChartLevel.Berceau, 120.0));
        string cascade = Instants(Humanizer.Humanize(source, Fingerprint, "0.1.0", ChartLevel.Cascade, 120.0));

        Assert.NotEqual(cradle, cascade);
    }

    [Fact]
    public void La_duree_d_un_tenu_varie_dans_les_bornes_annoncees()
    {
        // Les bornes sont des facteurs, pas des durees : un tenu de deux
        // secondes peut legitimement aller jusqu'a trois.
        Note[] source = [Note.Hold(0.0, KeyBinding.Grid(0), 1.0), Note.Hold(0.5, KeyBinding.Grid(1), 2.0)];

        IReadOnlyList<Note> result = Humanizer.Humanize(source, Fingerprint, "0.1.0", ChartLevel.Ronde, 120.0);

        for (int index = 0; index < source.Length; index++)
        {
            Assert.NotNull(result[index].Duration);
            Assert.InRange(result[index].Duration!.Value / source[index].Duration!.Value, 0.5, 1.5);
        }
    }

    [Fact]
    public void Un_tenu_sans_variation_de_duree_reste_exactement_long()
    {
        Note[] source = [Note.Hold(0.0, KeyBinding.Grid(0), 1.0)];
        HumanizerOptions fixedLength = new(HoldMinimumFactor: 1.0, HoldMaximumFactor: 1.0);

        IReadOnlyList<Note> result = Humanizer.Humanize(source, Fingerprint, "0.1.0", ChartLevel.Ronde, 120.0, fixedLength);

        Assert.Equal(1.0, result[0].Duration!.Value, 12);
    }

    [Fact]
    public void Une_frappe_ne_prend_pas_de_duree()
    {
        IReadOnlyList<Note> result = Humanizer.Humanize(Line(8), Fingerprint, "0.1.0", ChartLevel.Ronde, 120.0);

        Assert.All(result, note => Assert.Null(note.Duration));
    }
}

/// <summary>Le nettoyage d'une partition avant ecriture.</summary>
public class NoteCleanerTests
{
    [Fact]
    public void Le_seuil_de_fusion_vaut_dix_millisecondes()
    {
        Assert.Equal(0.010, NoteCleaner.MergeSeconds, 12);
    }

    [Theory]
    [InlineData(19.0, true)]
    [InlineData(19.001, false)]
    [InlineData(20.0, false)]
    public void Une_note_trop_tardive_pour_etre_jouee_est_retiree(double time, bool kept)
    {
        // La derniere seconde ne sert a rien : le joueur n'a plus de temps de
        // reactif avant la fin, et une note isolee la fin est un piege.
        IReadOnlyList<Note> result = NoteCleaner.Clean([Note.Tap(time, KeyBinding.Grid(0))], 20.0, 0.0);

        Assert.Equal(kept, result.Count == 1);
    }

    [Theory]
    [InlineData(0.749, false)]
    [InlineData(0.750, true)]
    public void Une_note_avant_le_premier_onset_est_retiree(double time, bool kept)
    {
        IReadOnlyList<Note> result = NoteCleaner.Clean([Note.Tap(time, KeyBinding.Grid(0))], 20.0, 0.75);

        Assert.Equal(kept, result.Count == 1);
    }

    [Theory]
    [InlineData(0.0, null)]
    [InlineData(-1.0, null)]
    [InlineData(0.5, 0.5)]
    public void Une_duree_non_positive_devient_une_absence_de_tenu(double duration, double? expected)
    {
        Note note = Note.Tap(1.0, KeyBinding.Grid(0)) with { Duration = duration };

        Note result = NoteCleaner.Clean([note], 20.0, 0.0)[0];

        Assert.Equal(expected, result.Duration);
        Assert.Equal(duration > 0, result.IsHold);
    }

    [Fact]
    public void Deux_notes_identiques_a_moins_dix_millisecondes_en_font_une()
    {
        IReadOnlyList<Note> result = NoteCleaner.Clean(
            [Note.Tap(0.000, KeyBinding.Grid(3)), Note.Tap(0.005, KeyBinding.Grid(3))],
            20.0,
            0.0);

        // Pour le joueur, deux frappes a cinq millisecondes d'intervalle sur la
        // meme touche n'en font qu'une : la seconde n'est meme pas visible.
        Note only = Assert.Single(result);
        Assert.Equal(0.000, only.Time, 12);
        Assert.Null(only.Duration);
    }

    [Fact]
    public void La_fusion_garde_l_instant_de_la_premiere_et_la_plus_longue_fin()
    {
        IReadOnlyList<Note> result = NoteCleaner.Clean(
            [Note.Hold(0.000, KeyBinding.Grid(3), 1.0), Note.Hold(0.005, KeyBinding.Grid(3), 2.0)],
            20.0,
            0.0);

        Note only = Assert.Single(result);
        Assert.Equal(0.000, only.Time, 12);
        Assert.Equal(2.005, only.Duration!.Value, 9);
    }

    [Fact]
    public void Une_frappe_absorbe_le_tenu_qui_la_suivra()
    {
        IReadOnlyList<Note> result = NoteCleaner.Clean(
            [Note.Tap(0.000, KeyBinding.Grid(3)), Note.Hold(0.005, KeyBinding.Grid(3), 1.0)],
            20.0,
            0.0);

        Note only = Assert.Single(result);
        Assert.Equal(0.000, only.Time, 12);
        Assert.Equal(1.005, only.Duration!.Value, 9);
    }

    [Fact]
    public void Au_dela_du_seuil_de_fusion_rien_ne_fusionne()
    {
        IReadOnlyList<Note> result = NoteCleaner.Clean(
            [
                Note.Tap(0.000, KeyBinding.Grid(3)),
                Note.Tap(0.011, KeyBinding.Grid(3)),
                Note.Tap(0.020, KeyBinding.Grid(4)),
            ],
            20.0,
            0.0);

        // Le seuil est franchi pour les deux premieres notes, et des touches
        // differentes ne fusionnent jamais, meme a l'instant.
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void Le_nettoyage_preserve_l_ordre_et_les_touches()
    {
        Note[] source =
        [
            Note.Tap(1.0, KeyBinding.Grid(0)),
            Note.Tap(1.5, KeyBinding.Grid(4)),
            Note.Tap(2.0, KeyBinding.Grid(8)),
        ];

        IReadOnlyList<Note> result = NoteCleaner.Clean(source, 20.0, 0.0);

        Assert.Equal(source, result);
    }

    [Fact]
    public void Une_liste_vide_reste_vide()
    {
        Assert.Empty(NoteCleaner.Clean([], 20.0, 0.0));
    }
}
