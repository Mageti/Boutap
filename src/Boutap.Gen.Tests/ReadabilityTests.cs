// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Tests du lissage de lisibilite, regles L1 a L10 de generateur.md §7.
//
// Deux des dix regles ne peuvent pas s' declencher : L4 est temporairee a
// chaque instant ou L2 s'applique, et L8 l'est des la premiere paire de L1.
// Les tests qui les visent construisent la situation exacte et verifient que
// le constat rendu est bien celui de la regle qui couvre. C'est une propriete
// du code, pas une defaillance de test : elle vaut mieux ecrite que
// un constat en espérant qu'un jour la regle sera completement decalee.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Boutap.Core.Pack;
using Boutap.Gen.Compose;
using Xunit;

namespace Boutap.Gen.Tests;

/// <summary>Le lissage de lisibilite et les dix regles qui le gouvernent.</summary>
public class ReadabilityFilterTests
{
    /// <summary>Un morceau de contextes qu'on choisit soi-meme.</summary>
    private sealed class Context(double tempoBpm, params double[] silentSeconds) : IAudioContext
    {
        public double TempoBpm { get; } = tempoBpm;

        public bool IsSilentAt(double timeSeconds) => silentSeconds.Contains(timeSeconds);
    }

    private static Note[] Tap(double[] times, int[] keys) =>
        [.. times.Select((time, index) => Note.Tap(time, KeyBinding.Grid(keys[index])))];

    private static string Rules(ReadabilityReport report) =>
        string.Join(",", report.Removed.Select(finding => finding.Rule).Distinct().Order());

    [Fact]
    public void Une_partition_vide_ne_donne_rien_a_lisser()
    {
        ReadabilityReport report = ReadabilityFilter.Smooth([]);

        Assert.Empty(report.Notes);
        Assert.Empty(report.Removed);
        Assert.Empty(report.Findings);
    }

    [Fact]
    public void Une_liste_nulle_est_refusee()
    {
        Assert.Throws<ArgumentNullException>(() => ReadabilityFilter.Smooth(null!));
    }

    [Fact]
    public void La_premiere_note_est_toujours_conservee()
    {
        // Elle n'a rien devant elle : aucune regle ne peut la retirer, meme si
        // elle colle de tres pres au debut d'un tenu.
        ReadabilityReport report = ReadabilityFilter.Smooth(
            [Note.Hold(0.0, KeyBinding.Grid(0), 1.0)]);

        Assert.Single(report.Notes);
        Assert.Equal(1.0, report.Notes[0].EndTime, 9);
    }

    [Fact]
    public void L1_retire_une_note_trop_proche_de_la_meme_touche()
    {
        // Deux coups sur le meme bouton a cinquante millisecondes ne sont pas
        // deux notes, c'est un tremblement de main.
        ReadabilityReport tooClose = ReadabilityFilter.Smooth(Tap([0.0, 0.050], [0, 0]));

        Assert.Single(tooClose.Notes);
        Assert.Equal("L1", Rules(tooClose));
        Assert.Equal(1, tooClose.Removed[0].NoteIndex);
    }

    [Fact]
    public void L1_laisse_passer_la_meme_touche_au_dela_du_seuil()
    {
        // Cent millisecondes : la limite exacte de la regle, et le debut de
        // ce qu'un joueur peut enchainer sur un bouton.
        ReadabilityReport report = ReadabilityFilter.Smooth(Tap([0.0, 0.090], [0, 0]));

        Assert.Equal(2, report.Notes.Count);
        Assert.Empty(report.Removed);
    }

    [Fact]
    public void L2_retire_une_note_trop_proche_d_une_touche_voisine()
    {
        // Deux touches differentes a cinquante millisecondes : le passage
        // d'un bouton a l'autre n'a pas eu le temps de se faire.
        ReadabilityReport report = ReadabilityFilter.Smooth(Tap([0.0, 0.050], [0, 3]));

        Assert.Single(report.Notes);
        Assert.Equal("L2", Rules(report));
    }

    [Fact]
    public void L2_laisse_passer_deux_touches_differentes_au_dela_du_seuil()
    {
        // Soixante millisecondes : au-dela, la regle se tait. Le seuil est
        // volontairement plus court que celui de L1, parce que changer de
        // bouton coute moins que de re-frapper le meme.
        ReadabilityReport report = ReadabilityFilter.Smooth(Tap([0.0, 0.055], [0, 3]));

        Assert.Equal(2, report.Notes.Count);
    }

    [Fact]
    public void L3_retire_la_cinquieme_note_simultanee()
    {
        // Quatre notes au meme instant passent, la cinquieme sort. Quatre, ce
        // n'est pas un hasard : c'est le nombre de doigts d'une main sur les
        // trois premieres colonnes de la grille, majoration des deux volants.
        ReadabilityReport report = ReadabilityFilter.Smooth(Tap([0.0, 0.0, 0.0, 0.0, 0.0], [0, 1, 2, 3, 4]));

        Assert.Equal(4, report.Notes.Count);
        Assert.Equal("L3", Rules(report));
        Assert.Equal(4, report.Removed[0].NoteIndex);
    }

    [Fact]
    public void L3_compte_les_notes_a_l_instant_meme_et_rien_au_dela()
    {
        // La cinquieme note tombe, les deux suivantes non, alors qu'elles
        // s'ajoutent aux quatre premieres. Le compteur compte les notes a
        // l'instant meme, il ne s'additionne pas dans le temps : sans cela,
        // une rafale de type piano-roll serait amputee de sa moitie.
        ReadabilityReport report = ReadabilityFilter.Smooth(
            Tap([0.0, 0.0, 0.0, 0.0, 0.0, 0.100, 0.200], [0, 1, 2, 3, 4, 5, 6]));

        Assert.Equal(6, report.Notes.Count);
        Assert.Equal("L3", Rules(report));
    }

    [Fact]
    public void L4_ne_peut_pas_s_declencher_parce_que_L2_passe_avant()
    {
        // La situation visee par L4 : deux touches voisines a vingt
        // millisecondes. C'est exactement ce que L2 interdit, et L2 est
        // evaluee dans le meme passage, juste avant. L4 est donc un filet que
        // L4 rend inutile : la regle la plus severe n'a jamais l'occasion de
        // parler tant que L2 est en place.
        ReadabilityReport report = ReadabilityFilter.Smooth(Tap([0.0, 0.020], [0, 1]));

        Assert.Equal("L2: deux notes de touches differentes trop rapprochees.", report.Removed[0].Reason);

        // La raison que L4 aurait rendue, « L4: deux notes de touches voisines
        // trop rapprochees. », n'est produite par aucune entree. Elle
        // redeviendrait du code vivant si le seuil de L2 passait un jour au
        // dessous des vingt-cinq millisecondes de L4.
        Assert.Equal(0.025, ReadabilityRules.AdjacentKeySeconds);
        Assert.True(ReadabilityRules.AdjacentKeySeconds < ReadabilityRules.DifferentKeySeconds);
    }

    [Fact]
    public void L5_retire_une_note_de_trop_dans_une_rafale_courte()
    {
        // Quatre notes au meme instant puis une cinquieme soixante-dix
        // millisecondes plus tard : cinq notes en cent millisecondes. Aucune
        // paire n'est assez proche pour tomber sous L1 ou L2, et pourtant la
        // rafale est injouable : c'est exactement ce que L5 voit.
        ReadabilityReport report = ReadabilityFilter.Smooth(
            Tap([0.0, 0.0, 0.0, 0.0, 0.070], [0, 1, 2, 3, 4]));

        Assert.Equal(4, report.Notes.Count);
        Assert.Equal("L5", Rules(report));
        Assert.Equal(4, report.Removed[0].NoteIndex);
    }

    [Fact]
    public void L5_compte_sur_la_derniere_quatrieme_note_et_non_sur_le_debut()
    {
        // Quatre notes a zéro, puis une cinquieme a cent millisecondes pile :
        // l'ecart vaut exactement le seuil, donc la regle se tait. C'est un
        // seuil ferme, pas une moyenne.
        ReadabilityReport report = ReadabilityFilter.Smooth(
            Tap([0.0, 0.0, 0.0, 0.0, 0.100], [0, 1, 2, 3, 4]));

        Assert.Equal(5, report.Notes.Count);
    }

    [Fact]
    public void L6_retire_la_vingtseptieme_note_dans_la_seconde()
    {
        // Seize notes a soixante millisecondes, soit neuf cent millisecondes
        // et demie : la dix-septeme met seize notes en moins d'une seconde et
        // tombe. Les touches alternent pour que L1 ne s'en mele pas.
        double[] times = [.. Enumerable.Range(0, 17).Select(index => index * 0.060)];
        int[] keys = [.. Enumerable.Range(0, 17).Select(index => index % 9)];

        ReadabilityReport report = ReadabilityFilter.Smooth(Tap(times, keys));

        Assert.Equal(16, report.Notes.Count);
        Assert.Equal("L6", Rules(report));
        Assert.Equal(16, report.Removed[0].NoteIndex);
    }

    [Fact]
    public void L6_attend_la_seizieme_note_avant_de_parler()
    {
        // Quinze notes sur la meme densite passent : le seuil porte sur seize
        // notes en une seconde, pas sur une densite moyenne.
        double[] times = [.. Enumerable.Range(0, 16).Select(index => index * 0.060)];
        int[] keys = [.. Enumerable.Range(0, 16).Select(index => index % 9)];

        ReadabilityReport report = ReadabilityFilter.Smooth(Tap(times, keys));

        Assert.Equal(16, report.Notes.Count);
        Assert.Empty(report.Removed);
    }

    [Fact]
    public void L7_retire_une_note_collee_a_la_fin_d_un_tenu()
    {
        // La note tombe trente millisecondes avant la fin de la tenue : le
        // joueur n'a pas encore releve le doigt qu'on lui en demande un autre.
        ReadabilityReport report = ReadabilityFilter.Smooth(
            [Note.Hold(0.0, KeyBinding.Grid(0), 1.0), Note.Tap(0.970, KeyBinding.Grid(3))]);

        Assert.Single(report.Notes);
        Assert.Equal("L7", Rules(report));
        Assert.Equal(1, report.Removed[0].NoteIndex);
    }

    [Fact]
    public void L7_laisse_passer_une_note_bien_ecartee_de_la_fin_du_tenu()
    {
        // Deux centimes de plus, et la regle se tait. Le seuil est celui de la
        // regle : quarante millisecondes.
        ReadabilityReport report = ReadabilityFilter.Smooth(
            [Note.Hold(0.0, KeyBinding.Grid(0), 1.0), Note.Tap(0.930, KeyBinding.Grid(3))]);

        Assert.Equal(2, report.Notes.Count);
    }

    [Fact]
    public void L7_cote_debut_est_couverte_par_L1_et_L2()
    {
        // Une note sur le meme bouton que le tenu, a vingt millisecondes :
        // L1 parle avant que L7 n'ait l'occasion de distinguer le debut de la
        // fin. Ce n'est pas une perte : la note tombe de toute facon.
        ReadabilityReport report = ReadabilityFilter.Smooth(
            [Note.Hold(0.0, KeyBinding.Grid(0), 1.0), Note.Tap(0.020, KeyBinding.Grid(0))]);

        Assert.Single(report.Notes);
        Assert.Equal("L1", Rules(report));
    }

    [Fact]
    public void L8_ne_peut_pas_s_declencher_parce_que_L1_passe_avant()
    {
        // La situation visee par L8 : une rafale de six notes du meme bouton.
        // Elles ne sont pas simultanees, donc L3 ne les voit pas, et chacune
        // est a quatre-vingts millisecondes de la precedente, donc L1 non
        // plus. En pratique L1 a deja retire cinq des six bien avant que le
        // compteur de rafale n'atteigne jamais son seuil de six.
        ReadabilityReport report = ReadabilityFilter.Smooth(
            Tap([0.0, 0.080, 0.160, 0.240, 0.320, 0.400], [3, 3, 3, 3, 3, 3]));

        Assert.Equal("L1", Rules(report));
        Assert.Equal(6, ReadabilityRules.MaxPerBurst);

        // L1 garde une note sur deux. Les survivantes sont alors espacees de
        // cent soixante millisecondes, plus que le seuil de L1 : le compteur
        // de rafale repart de un a chaque note et n'approche jamais de six.
        Assert.Equal(
            "0.000,0.160,0.320",
            string.Join(",", report.Notes.Select(note => note.Time.ToString("0.000", CultureInfo.InvariantCulture))));
    }

    [Fact]
    public void Le_premier_constat_rencontre_est_le_seul_rapporte()
    {
        // Cette note viole L1 et L5 en meme temps. Un lissage qui rapportait
        // les deuxraits une note comme retiree deux fois, ce qui rendrait le
        // rapport faux. Un seul constat, le premier dans l'ordre.
        ReadabilityReport report = ReadabilityFilter.Smooth(
            Tap([0.0, 0.0, 0.0, 0.0, 0.020], [0, 1, 2, 3, 0]));

        ReadabilityFinding finding = Assert.Single(report.Removed);
        Assert.Equal("L1", finding.Rule);
        Assert.Equal(4, report.Notes.Count);
    }

    [Fact]
    public void Le_lissage_ne_signale_rien_il_ne_fait_que_retirer()
    {
        // L9 et L10 ne retirent aucune note : elles vivent dans un contexte que
        // le lissage n'a pas. Smooth renvoie donc un rapport sans constat, et
        // c'est Inspect qui produit les signalements.
        ReadabilityReport report = ReadabilityFilter.Smooth(Tap([0.0, 0.050], [0, 0]));

        Assert.Empty(report.Findings);
        Assert.Single(report.Removed);
    }

    [Fact]
    public void Les_notes_survivantes_gardent_leur_instant_leur_touche_et_leur_ordre()
    {
        // Le lissage ne touche qu'aux instants : ni une note ecartee par une
        // regle ni une note acceptee ne doit voir son instant bouge.
        double[] times = [0.0, 0.10, 0.16, 0.30, 0.50];
        int[] keys = [0, 1, 2, 3, 4];
        Note[] source = Tap(times, keys);

        ReadabilityReport report = ReadabilityFilter.Smooth(source);

        // Le lissage ne touche qu'aux instants : une note ecartee disparait,
        // une note acceptee revient identique. Comparer les notes entieres
        // dit les deux d'un coup.
        Assert.Equal(times.Length, report.Notes.Count);
        for (int index = 0; index < times.Length; index++)
        {
            Assert.Equal(Note.Tap(times[index], KeyBinding.Grid(keys[index])), report.Notes[index]);
        }
    }

    [Fact]
    public void L9_signale_un_trou_de_plus_de_quatre_temps_entre_deux_zones_denses()
    {
        // Cinq notes en un demi-temps, puis cinq notes trois secondes plus
        // tard. Le trou fait cinq temps a cent vingt : c'est le genre de trou
        // qu'un joueur lit comme une fin de morceau, puis comme un debut.
        double[] times = [0.0, 0.1, 0.2, 0.3, 0.4, 2.9, 3.0, 3.1, 3.2, 3.3];
        int[] keys = [.. Enumerable.Range(0, 10).Select(index => index % 9)];

        IReadOnlyList<ReadabilityFinding> findings = ReadabilityFilter.Inspect(Tap(times, keys), new Context(120.0));

        ReadabilityFinding finding = Assert.Single(findings);
        Assert.Equal("L9", finding.Rule);
        Assert.Equal(5, finding.NoteIndex);
    }

    [Fact]
    public void L9_laisse_passer_un_trou_plus_court_que_quatre_temps()
    {
        // Un demi-temps de trous : deux fois moins que le seuil. La regle ne
        // confond pas une respiration et un morceau casse en deux.
        double[] times = [0.0, 0.1, 0.2, 0.3, 0.4, 0.9, 1.0, 1.1, 1.2, 1.3];
        int[] keys = [.. Enumerable.Range(0, 10).Select(index => index % 9)];

        Assert.Empty(ReadabilityFilter.Inspect(Tap(times, keys), new Context(120.0)));
    }

    [Fact]
    public void L9_ne_signale_rien_si_le_tempo_est_inconnu()
    {
        // Sans tempo mesure, une pause de dix secondes entre deux notes
        // n'est pas un trou de quatre temps, c'est un morceau sans tempo
        // connu. Signaler ici serait une affirmation sans preuve, alors que
        // le signalement ne retire rien de toute facon : mieux vaut se taire.
        ReadabilityReport report = ReadabilityFilter.Smooth(Tap([0.0, 10.0], [0, 1]));

        Assert.Empty(ReadabilityFilter.Inspect(report.Notes, new Context(0.0)));
    }

    [Fact]
    public void L10_signale_une_note_posee_dans_un_silence()
    {
        // Une note a la deuxieme seconde d'un morceau ou le deuxieme temps est
        // muet. Elle est impossible a entendre, donc impossible a jouer.
        IReadOnlyList<ReadabilityFinding> findings = ReadabilityFilter.Inspect(
            Tap([0.0, 1.0, 2.0], [0, 1, 2]),
            new Context(120.0, 1.0));

        ReadabilityFinding finding = Assert.Single(findings);
        Assert.Equal("L10", finding.Rule);
        Assert.Equal(1, finding.NoteIndex);
    }

    [Fact]
    public void L10_ne_signale_rien_l_a_ou_le_morceau_resonne()
    {
        IReadOnlyList<ReadabilityFinding> findings = ReadabilityFilter.Inspect(
            Tap([0.0, 0.5, 1.0, 1.5], [0, 1, 2, 3]),
            new Context(120.0));

        Assert.Empty(findings);
    }

    [Fact]
    public void Sans_contexte_rien_nest_signale()
    {
        // Inspect a besoin de savoir ce que le fichier contient. Sans cette
        // information, il ne peut rien affirmer, et il ne devine pas.
        double[] times = [0.0, 10.0];
        int[] keys = [0, 1];

        Assert.Empty(ReadabilityFilter.Inspect(Tap(times, keys), null));
        Assert.Empty(ReadabilityFilter.Inspect([], new Context(120.0)));
    }

    [Fact]
    public void Les_constats_de_l_inspection_porte_sur_les_notes_et_non_sur_les_ecarts()
    {
        // L9 se signale sur la premiere note apres le trou, L10 sur la note
        // elle-meme. Ce sont deux conventions differentes, parce que l'une
        // parle d'une absence et l'autre d'une note impossible.
        double[] times = [0.0, 0.1, 0.2, 0.3, 0.4, 2.9, 3.0, 3.1, 3.2, 3.3];
        int[] keys = [.. Enumerable.Range(0, 10).Select(index => index % 9)];

        IReadOnlyList<ReadabilityFinding> findings = ReadabilityFilter.Inspect(
            Tap(times, keys),
            new Context(120.0, 3.3));

        // Le trou est signale sur la premiere note qui le suit, la note muette
        // sur elle-meme : deux index differents, parce que l'un parle d'une
        // absence et l'autre d'une note impossible.
        Assert.Equal("L9,L10", string.Join(",", findings.Select(finding => finding.Rule)));
        Assert.Equal("5,9", string.Join(",", findings.Select(finding => finding.NoteIndex)));
    }
}
