// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Tests de la composition : pliage des hauteurs, accords, quantification,
// courbe d'effort. Rien ici ne lit de fichier : tout est mesure sur des
// entrees construites a la main, pour que l'echec dise quelle valeur est
// fausse et non quel fichier de reference a change.

using System;
using System.Collections.Generic;
using System.Linq;
using Boutap.Core.Common;
using Boutap.Core.Pack;
using Boutap.Gen.Analysis;
using Boutap.Gen.Compose;
using Xunit;

namespace Boutap.Gen.Tests;

/// <summary>Le passage de douze classes de hauteur a neuf touches.</summary>
public class ChromaFoldTests
{
    [Fact]
    public void Les_neuf_classes_conservees_sont_les_deux_echelles_diatoniques()
    {
        // Les neuf sons d'une gamme diatonique : majeure et mineure naturelle
        // reunies. C'est exactement ce qu'une manette a neuf boutons sait
        // jouer, et c'est pourquoi la grille existe.
        Assert.Equal("0,2,3,5,7,8,9,10,11", string.Join(",", ChromaFold.KeptClasses));
        Assert.Equal(ChromaFold.GridSize, ChromaFold.KeptClasses.Count);
    }

    [Fact]
    public void Les_trois_classes_retirees_sont_ceux_qu_on_ne_peut_pas_plier_sans_se_tromper()
    {
        // 1 (deuxieme mineure), 4 (tierce majeure) et 6 (tritone) sont les
        // seules classes absentes du gabarit. Les douze se repartissent donc
        // en neuf touches plus trois plis.
        int[] removed = [.. Enumerable.Range(0, 12).Where(pitch => !ChromaFold.KeptClasses.Contains(pitch))];
        Assert.Equal("1,4,6", string.Join(",", removed));
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(4, 5)]
    [InlineData(6, 7)]
    public void Une_classe_retiree_se_pliche_vers_la_classe_conservee_la_plus_proche(int pitchClass, int expected)
    {
        // A egalite de distance (1 est a mi-chemin de 0 et de 2), on plie vers
        // le haut : plier vers le bas ferait disparaitre la note dans une
        // classe deja presente plus bas.
        Assert.Equal(expected, ChromaFold.Fold(pitchClass));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(11, 11)]
    [InlineData(12, 0)]
    [InlineData(13, 2)]
    [InlineData(-1, 11)]
    [InlineData(-2, 10)]
    public void Le_plicage_tourne_avec_la_classe_de_hauteur(int pitchClass, int expected)
    {
        // Les hauteurs peuvent sortir de la octave, et meme la traverser par
        // le bas : le pliage est une operation de groupe, pas une recherche.
        Assert.Equal(expected, ChromaFold.Fold(pitchClass));
    }

    [Fact]
    public void L_index_de_touche_est_la_place_dans_les_neuf_classes()
    {
        // T0 et T1 sont le do et le re, deux cases apart dans la grille, parce
        // que la premiere retiree s'est pliee sur la deuxieme.
        Assert.Equal(0, ChromaFold.GridIndex(0));
        Assert.Equal(1, ChromaFold.GridIndex(2));
        Assert.Equal(8, ChromaFold.GridIndex(11));
        Assert.Equal(1, ChromaFold.GridIndex(1));
    }

    [Fact]
    public void Une_touche_redonne_sa_classe_de_hauteur()
    {
        // Aller-retour : ce qui entre par la droite ressort a l'identique.
        for (int pitchClass = 0; pitchClass < 12; pitchClass++)
        {
            int folded = ChromaFold.Fold(pitchClass);
            Assert.Equal(folded, ChromaFold.PitchClassAt(ChromaFold.GridIndex(pitchClass)));
        }
    }

    [Fact]
    public void Un_index_de_touche_hors_grille_est_refuse()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ChromaFold.PitchClassAt(ChromaFold.GridSize));
        Assert.Throws<ArgumentOutOfRangeException>(() => ChromaFold.PitchClassAt(-1));
    }

    [Fact]
    public void Le_chroma_replie_conserve_le_total_et_regroupe_les_classes_retirees()
    {
        // Chaque classe de hauteur tombe dans une touche et une seule : le
        // repli ne perd aucune energie, il la deplace seulement. C'est ce qui
        // distingue un repli d'une troncature.
        double[] chroma = new double[12];
        chroma[0] = 1.0;
        chroma[1] = 2.0;
        chroma[4] = 4.0;
        chroma[6] = 8.0;
        chroma[9] = 16.0;

        double[] folded = ChromaFold.FoldChroma(chroma);

        Assert.Equal(ChromaFold.GridSize, folded.Length);
        Assert.Equal(31.0, folded.Sum(), 9);
        Assert.Equal(2.0, folded[ChromaFold.GridIndex(1)], 9);
        Assert.Equal(4.0, folded[ChromaFold.GridIndex(4)], 9);
        Assert.Equal(8.0, folded[ChromaFold.GridIndex(6)], 9);
        Assert.Equal(1.0, folded[ChromaFold.GridIndex(0)], 9);
        Assert.Equal(16.0, folded[ChromaFold.GridIndex(9)], 9);
    }

    [Fact]
    public void Un_chroma_de_douze_classes_est_exige()
    {
        double[] eleven = new double[11];
        Assert.Throws<ArgumentException>(() => ChromaFold.FoldChroma(eleven));
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(0, 1, 1)]
    [InlineData(0, 2, 2)]
    [InlineData(0, 3, 1)]
    [InlineData(0, 4, 2)]
    [InlineData(0, 6, 2)]
    [InlineData(0, 8, 4)]
    public void La_distance_de_manhattan_compte_les_deux_axes(int left, int right, int expected)
    {
        // On separe les mains, on ne contourne pas les touches : un pas
        // lateral et un pas vertical valent pareil. T0 vers T8 est un coin, il
        // vaut 2 et 2, soit 4.
        Assert.Equal(expected, ChromaFold.Distance(left, right));
    }

    [Fact]
    public void Seules_les_touches_qui_se_touchent_sont_voisines()
    {
        Assert.True(ChromaFold.AreAdjacent(0, 1));
        Assert.True(ChromaFold.AreAdjacent(0, 3));
        Assert.True(ChromaFold.AreAdjacent(4, 5));
        Assert.False(ChromaFold.AreAdjacent(0, 0));
        Assert.False(ChromaFold.AreAdjacent(0, 2));
        Assert.False(ChromaFold.AreAdjacent(0, 4));
        Assert.False(ChromaFold.AreAdjacent(0, 8));
    }
}

/// <summary>Le choix de l'accord dominant, d'apres generateur.md §5.2.</summary>
public class ChordMapperTests
{
    [Fact]
    public void Les_vingt_quatre_accords_couvrent_chacune_des_douze_toniques()
    {
        IReadOnlyList<Chord> all = ChordMapper.Candidates;
        Assert.Equal(24, all.Count);
        Assert.Equal(24, all.Select(chord => (chord.Root, chord.Mode)).Distinct().Count());

        for (int root = 0; root < 12; root++)
        {
            foreach (KeyMode mode in (KeyMode[])[KeyMode.Major, KeyMode.Minor])
            {
                Assert.Equal((root, mode), (ChordMapper.Of(root, mode).Root, ChordMapper.Of(root, mode).Mode));
            }
        }
    }

    [Fact]
    public void Un_accord_tonique_est_tombe_meme_de_vingt_quatre_fois()
    {
        // La racine 24 fois, ce qui n'est pas la meme chose qu'une tonique.
        Assert.Equal(ChordMapper.Of(0, KeyMode.Major), ChordMapper.Of(24, KeyMode.Major));
        Assert.Equal(ChordMapper.Of(0, KeyMode.Major), ChordMapper.Of(-12, KeyMode.Major));
    }

    [Fact]
    public void Un_accord_majeur_porte_la_tierce_et_la_quinte_majeures()
    {
        Assert.Equal("0,4,7", string.Join(",", ChordMapper.Of(0, KeyMode.Major).Intervals));
        Assert.Equal("0,3,7", string.Join(",", ChordMapper.Of(0, KeyMode.Minor).Intervals));
    }

    [Fact]
    public void Les_touches_d_un_accord_viennent_du_plicage_de_ses_trois_notes()
    {
        // Do majeur : do (T0), mi (T3, parce que fa tombe sur mi) et sol (T4).
        // Le repli n'ajoute aucune touche : il ne fait que renommer.
        Assert.Equal("0,3,4", string.Join(",", ChordMapper.Of(0, KeyMode.Major).GridNotes));
        Assert.Equal("0,2,4", string.Join(",", ChordMapper.Of(0, KeyMode.Minor).GridNotes));
        Assert.All(ChordMapper.Candidates, chord => Assert.All(chord.GridNotes, key => Assert.InRange(key, 0, 8)));
    }

    [Fact]
    public void Le_meilleur_accord_est_celui_que_le_chroma_explique()
    {
        // L'aiguille sur do, mi et sol : le chroma de l'accord de do majeur.
        double[] chroma = new double[12];
        chroma[0] = 1.0;
        chroma[4] = 0.9;
        chroma[7] = 0.8;
        chroma[2] = 0.05;

        Assert.Equal(ChordMapper.Of(0, KeyMode.Major), ChordMapper.Best(chroma));
    }

    [Fact]
    public void Un_chroma_sans_energie_designe_le_premier_accord()
    {
        // Aucun accord n'est mieux qu'un autre : le classement doit tout de
        // meme rendre un accord, parce qu'une partition ne peut pas etre vide.
        double[] chroma = new double[12];
        Assert.Equal(ChordMapper.Of(0, KeyMode.Major), ChordMapper.Best(chroma));
    }

    [Fact]
    public void Le_score_d_un_accord_somme_l_energie_de_ses_trois_notes()
    {
        // Le score est la somme brute, sans moyenne ni ponderation : sur une
        // fenetre d'un temps, une mesure savante n'aurait pas plus de signal.
        double[] chroma = new double[12];
        chroma[0] = 1.0;
        chroma[4] = 0.9;
        chroma[7] = 0.8;

        Assert.Equal(2.7, ChordMapper.Score(chroma, ChordMapper.Of(0, KeyMode.Major)), 9);
    }

    [Fact]
    public void Un_chroma_de_douze_classes_est_exige()
    {
        double[] eleven = new double[11];
        Assert.Throws<ArgumentException>(() => ChordMapper.Best(eleven));
    }

    [Theory]
    [InlineData(0, "C")]
    [InlineData(1, "Db")]
    [InlineData(9, "A")]
    [InlineData(11, "B")]
    public void La_tonique_s_ecrit_en_graphie_plate(int root, string expected)
    {
        Assert.Equal(expected, ChordMapper.Of(root, KeyMode.Major).TonicName);
    }
}

/// <summary>La quantification sur la grille rythmique, d'apres generateur.md §4.9.</summary>
public class QuantizerTests
{
    private const double Step = 60.0 / 120.0 / Quantizer.SubdivisionsPerBeat;

    [Fact]
    public void La_grille_a_seize_sous_cases_par_temps()
    {
        // Seize sous-cases, c'est la resolution du format : en dessous, une
        // variation n'aurait plus nowhere to aller.
        Assert.Equal(16, Quantizer.SubdivisionsPerBeat);
    }

    [Fact]
    public void Sans_onset_ou_sans_battement_il_n_y_a_rien_a_proposer()
    {
        IReadOnlyList<Onset> onsets = [new Onset(4, 0.5, 1.0)];

        Assert.Empty(Quantizer.Quantize([], [0.0, 0.5], 120.0, []));
        Assert.Empty(Quantizer.Quantize(onsets, [], 120.0, []));
        Assert.Empty(Quantizer.Quantize(onsets, [0.0, 0.5], 0.0, []));
        Assert.Empty(Quantizer.Quantize(onsets, [0.0, 0.5], -120.0, []));
    }

    [Fact]
    public void Un_onset_pose_sur_le_battement_tombe_sur_la_premiere_sous_case()
    {
        // 120 BPM : le temps dure une demi-seconde, la sous-case 1/120 s.
        IReadOnlyList<GridTime> grid = Quantize([new Onset(0, 0.5, 1.0)]);

        Assert.Single(grid);
        Assert.Equal(0, grid[0].Subdivision);
        Assert.Equal(0.5, grid[0].Seconds, 9);
    }

    [Fact]
    public void Un_onset_plus_loin_tombe_sur_la_sous_case_correspondante()
    {
        // Trois sous-cases apres le battement : c'est la que l'energie du
        // temps doit tomber, quelle qu'elle soit.
        IReadOnlyList<GridTime> grid = Quantize([new Onset(0, 0.5 + (3 * Step), 1.0)]);

        Assert.Equal(3, grid[0].Subdivision);
        Assert.Equal(0.5 + (3 * Step), grid[0].Seconds, 9);
    }

    [Fact]
    public void La_phase_la_plus_energique_du_temps_devient_la_cible()
    {
        // Quand la caisse claire tombe a contretemps, c'est elle qui fixe le
        // point de depart. Ici la phase forte est la troisieme : le battement
        // detecte se retrouve donc marque sous-case 3, sans changer d'instant.
        IReadOnlyList<BeatPhase> phases = [new BeatPhase(0.5, 0.5, [0, 0, 1, 0], StrongestPhase: 2)];

        IReadOnlyList<GridTime> grid = Quantizer.Quantize(
            [new Onset(0, 0.5, 1.0)], [0.5, 1.0], 120.0, phases);

        Assert.Equal(2, grid[0].Subdivision);
        Assert.Equal(0.5, grid[0].Seconds, 9);
    }

    [Fact]
    public void Sans_phase_mesuree_le_battement_lui_meme_est_la_cible()
    {
        // Pas de mesure de phase : la grille est reguliere, donc la premiere
        // sous-case est la reference. C'est ce que dit la specification, et
        // c'est le repli quand les seize sous-cases sont toutes faibles.
        IReadOnlyList<GridTime> grid = Quantizer.Quantize([new Onset(0, 0.5, 1.0)], [0.5, 1.0], 120.0, []);

        Assert.Equal(0, grid[0].Subdivision);
    }

    [Fact]
    public void Un_onset_une_periode_plus_loin_est_un_temps_et_non_la_derniere_sous_case()
    {
        // 60 BPM : le temps dure une seconde et il y a seize sous-cases. Un
        // onset pose une seconde plus loin tombe pile sur la sous-case 16,
        // qui n'existe pas : il appartient au temps suivant.
        IReadOnlyList<GridTime> grid = Quantizer.Quantize([new Onset(0, 1.0, 1.0)], [0.0], 60.0, []);

        Assert.Equal(0, grid[0].Subdivision);
        Assert.Equal(1.0, grid[0].Seconds, 9);
    }

    [Fact]
    public void Un_onset_avant_le_battement_est_ramene_sur_le_battement()
    {
        // Un battement est recherche dans la demi-periode qui precede, donc
        // un onset peut se retrouver avant lui. Le ramenage se fait vers le
        // battement, jamais vers zero : une note a 0,2 s serait un coup que le
        // joueur entendrait avant le morceau.
        IReadOnlyList<GridTime> grid = Quantize([new Onset(0, 0.26, 1.0)]);

        Assert.Equal(0, grid[0].Subdivision);
        Assert.Equal(0.5, grid[0].Seconds, 9);
    }

    [Fact]
    public void L_amplitude_du_depart_passe_sans_etre_arrondie()
    {
        // La force est une amplitude normalisee, pas une note : elle traverse
        // la quantification telle quelle, sans etre retrecie sur 0-8. C'est
        // ChartGenerator qui fait la conversion, au moment du choix d'accord.
        IReadOnlyList<GridTime> grid = Quantize([new Onset(0, 0.5, 0.37)]);

        Assert.Equal(0.37, grid[0].Strength, 9);
    }

    [Fact]
    public void Une_sous_case_sur_le_temps_avant_zero_ne_recule_pas_derriere()
    {
        // Le premier battement est a 0,5 s et l'onset a 0,24 s : il est
        // avant la grille. La proposition reste dans le morceau.
        IReadOnlyList<GridTime> grid = Quantize([new Onset(0, 0.24, 1.0)]);

        Assert.True(grid[0].Seconds >= 0.0);
        Assert.Equal(0.5, grid[0].Seconds, 9);
    }

    private static IReadOnlyList<GridTime> Quantize(IReadOnlyList<Onset> onsets) =>
        Quantizer.Quantize(onsets, [0.5, 1.0], 120.0, []);
}

/// <summary>La courbe d'effort et la note de difficulte, d'apres generateur.md §9.1.</summary>
public class StrainCurveTests
{
    [Fact]
    public void Une_partition_vide_donne_un_profil_vide()
    {
        StrainProfile profile = StrainCurve.Compute([]);

        Assert.Empty(profile.TimesSeconds);
        Assert.Empty(profile.Values);
        Assert.Equal(0.0, profile.Peak);
        Assert.Equal(0.0, profile.PeakTimeSeconds);
    }

    [Fact]
    public void Une_note_unique_relache_exponentiellement_a_partir_de_son_effort()
    {
        // Une note frappee vaut 1 d'effort, puis la main relaxe : la courbe
        // decroit de facon exponentielle, jamais lineairement.
        StrainProfile profile = StrainCurve.Compute([Note.Tap(0.0, KeyBinding.Grid(0))]);

        Assert.Equal(1.0, profile.Peak, 9);
        Assert.Equal(0.0, profile.PeakTimeSeconds, 9);
        Assert.Equal(StrainEffort.Tap, profile.Values[0], 9);
        Assert.Equal(StrainEffort.Tap * Math.Exp(-StrainCurve.StepSeconds / StrainCurve.ReleaseSeconds), profile.Values[1], 9);
    }

    [Fact]
    public void La_courbe_va_decroissant_jusqu_apres_la_derniere_note()
    {
        // Sans ce rabattement, la courbe finirait sur une valeur qui n'a pas
        // encore eu le temps de retomber, et le pic de fin serait un faux pic.
        Note note = Note.Hold(0.0, KeyBinding.Grid(0), 1.0);
        StrainProfile profile = StrainCurve.Compute([note]);

        double expectedEnd = note.EndTime + StrainCurve.ReleaseSeconds;
        Assert.True(
            profile.TimesSeconds[^1] >= expectedEnd - 1e-9,
            "la courbe doit couvrir toute la relaxation, pas s'arrêter à la fin de la tenue");
    }

    [Fact]
    public void Une_tenue_coute_plus_cher_qu_une_note_frappee()
    {
        // La main reste posee : c'est le coup qui coute, pas la frappe.
        Assert.Equal(StrainEffort.Hold, StrainCurve.Effort(Note.Hold(0.0, KeyBinding.Grid(0), 0.5)));
        Assert.Equal(StrainEffort.Tap, StrainCurve.Effort(Note.Tap(0.0, KeyBinding.Grid(0))));
        Assert.Equal(StrainEffort.Tap, StrainCurve.Effort(Note.Tap(0.0, KeyBinding.Grid(0)) with { Duration = 0 }));

        StrainProfile held = StrainCurve.Compute([Note.Hold(0.0, KeyBinding.Grid(0), 0.5)]);
        Assert.Equal(StrainEffort.Hold, held.Peak, 9);
    }

    [Fact]
    public void L_effort_d_un_groupe_depend_du_nombre_de_notes()
    {
        Note note = Note.Tap(0.0, KeyBinding.Grid(0));

        Assert.Equal(0.0, StrainCurve.GroupEffort([note], 0));
        Assert.Equal(StrainEffort.Tap, StrainCurve.GroupEffort([note], 1));
        Assert.Equal(StrainEffort.Chord, StrainCurve.GroupEffort([note], 2));
        Assert.Equal(StrainEffort.Jack, StrainCurve.GroupEffort([note], 3));
        Assert.Equal(StrainEffort.Jack, StrainCurve.GroupEffort([note], 7));
    }

    [Fact]
    public void Le_pic_de_strain_tombe_sur_le_temps_le_plus_charge()
    {
        // Deux notes a dix secondes d'ecart. La courbe ne doit pas monter
        // jusqu'au bout de la partition : son maximum est la ou l'effort
        // s'accumule, pas la derniere echantillon.
        Note[] notes = [Note.Tap(0.0, KeyBinding.Grid(0)), Note.Tap(10.0, KeyBinding.Grid(3))];
        StrainProfile profile = StrainCurve.Compute(notes);

        Assert.Equal(10.0, profile.PeakTimeSeconds, 9);
        Assert.True(profile.PeakTimeSeconds < profile.TimesSeconds[^1]);

        // Le pic vaut un peu plus d'une note frappee : a dix secondes de
        // distance, la premiere n'a pas fini de relaxer et laisse
        // exp(-10 / 1,2) ≈ 2,4·10⁻⁴ derriere elle. Ce n'est pas un residu
        // d'arrondi, c'est le modele, et il ne devient appreciable qu'a
        // quelques secondes d'ecart.
        Assert.Equal(StrainEffort.Tap, profile.Peak, 3);
    }

    [Fact]
    public void Un_accord_de_trois_notes_vaut_trois_efforts_dans_la_courbe()
    {
        // La courbe additionne note par note. GroupEffort, lui, regroupe le
        // groupe en un effort seul : ce sont deux mesures, et la difficulte
        // announcee se lit sur GroupEffort, pas sur le sommet de la courbe.
        Note[] chord =
        [
            Note.Tap(0.0, KeyBinding.Grid(0)),
            Note.Tap(0.0, KeyBinding.Grid(2)),
            Note.Tap(0.0, KeyBinding.Grid(4)),
        ];
        StrainProfile profile = StrainCurve.Compute(chord);

        Assert.Equal(3.0 * StrainEffort.Tap, profile.Peak, 9);
        Assert.Equal(0.0, profile.PeakTimeSeconds, 9);
        Assert.True(StrainCurve.GroupEffort(chord, 3) > profile.Peak);
    }

    [Fact]
    public void La_courbe_est_echantillonnee_tous_les_dix_millisecondes()
    {
        // Dix fois plus fin que la constante de relaxation n'apporterait rien :
        // on ne calcule pas plus finement que ce qu'on sait mesurer.
        Note[] notes = [Note.Tap(0.0, KeyBinding.Grid(0)), Note.Tap(1.0, KeyBinding.Grid(1))];
        StrainProfile profile = StrainCurve.Compute(notes);

        Assert.Equal(StrainCurve.StepSeconds, 0.1);
        Assert.Equal(profile.TimesSeconds.Count, profile.Values.Count);
        for (int step = 1; step < profile.TimesSeconds.Count; step++)
        {
            Assert.Equal(StrainCurve.StepSeconds, profile.TimesSeconds[step] - profile.TimesSeconds[step - 1], 9);
        }
    }
}
