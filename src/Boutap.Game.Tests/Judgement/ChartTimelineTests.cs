// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Boutap.Core.Pack;
using Boutap.Game.Judgement;
using Xunit;

namespace Boutap.Game.Tests.Judgement;

/// <summary>
/// La lecture d'un chart. Ces tests n'ont ni fenetre, ni carte graphique, ni
/// carte son : c'est tout l'interet de separer la logique de jeu du moteur.
/// </summary>
public sealed class ChartTimelineTests
{
    private static Note Tap(double time, int key = 0) => Note.Tap(time, KeyBinding.Grid(key));

    private static ChartTimeline Frise(params Note[] notes) => new(ChartLevel.Berceau, notes);

    [Fact]
    public void Une_Frise_Vide_Est_Valide()
    {
        ChartTimeline timeline = Frise();
        Assert.Equal(0, timeline.Count);
        Assert.Equal(0, timeline.LastTime);
        Assert.Empty(timeline.Visible(0, 5));
    }

    [Fact]
    public void Une_Liste_Nulle_Est_Refusee()
    {
        Assert.Throws<ArgumentNullException>(() => new ChartTimeline(ChartLevel.Berceau, null!));
    }

    [Fact]
    public void Des_Notes_Desordonnees_Sont_Refusees_Et_Non_Triees_En_Silence()
    {
        // Le validateur de packs signale deja chart.notes-unsorted. Trier ici
        // rendrait le bug invisible et le chart jouable quand meme, ce qui est
        // le pire des deux mondes : on prefere echouer tot.
        Note[] desordre = [Tap(2.0), Tap(1.0)];
        ArgumentException erreur = Assert.Throws<ArgumentException>(() => Frise(desordre));
        Assert.Contains("triees", erreur.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Des_Notes_De_Meme_Temps_Sont_Acceptes()
    {
        // Deux notes au meme temps forment un accord : l'ordre n'est pas
        // impose au sein d'un groupe, seule la non-decroissance compte.
        ChartTimeline timeline = Frise(Tap(1.0, 0), Tap(1.0, 1), Tap(2.0, 4));
        Assert.Equal(3, timeline.Count);
    }

    [Fact]
    public void L_Indexeur_Refuse_Les_Indices_Hors_Bornes()
    {
        ChartTimeline timeline = Frise(Tap(1.0), Tap(2.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => timeline[-1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => timeline[2]);
        Assert.Equal(2.0, timeline[1].Time);
    }

    [Fact]
    public void La_Fenetre_De_La_Frise_Vient_Du_Niveau()
    {
        ChartTimeline berceau = new(ChartLevel.Berceau, [Tap(1.0)]);
        ChartTimeline cascade = new(ChartLevel.Cascade, [Tap(1.0)]);
        Assert.Equal(JudgementWindows.ForLevel(ChartLevel.Berceau), berceau.BaseWindow);
        Assert.True(cascade.BaseWindow.Good < berceau.BaseWindow.Good);
    }

    [Fact]
    public void Une_Note_Sans_Multiplicateur_Utilise_La_Fenetre_Du_Niveau()
    {
        ChartTimeline timeline = Frise(Tap(1.0));
        Note note = timeline[0];
        Assert.Equal(JudgementWindows.ForLevel(ChartLevel.Berceau), timeline.WindowOf(note));
    }

    [Fact]
    public void Le_Multiplicateur_W_Retrecit_La_Fenetre_De_La_Note()
    {
        Note serree = Tap(1.0) with { WindowScale = 0.5 };
        ChartTimeline timeline = Frise(serree);
        Assert.Equal(0.070, timeline.WindowOf(serree).Good, 12);
    }

    [Fact]
    public void Le_Multiplicateur_W_Elargit_La_Fenetre_De_La_Note()
    {
        Note large = Tap(1.0) with { WindowScale = 3 };
        ChartTimeline timeline = Frise(large);
        Assert.Equal(0.420, timeline.WindowOf(large).Good, 12);
    }

    [Fact]
    public void Le_Coup_Evalue_L_Ecart_Entre_Le_Coup_Et_L_Arrivee()
    {
        Note note = Tap(4.0);
        ChartTimeline timeline = Frise(note);
        Assert.Equal(JudgementGrade.Perfect, timeline.Judge(note, 4.0));
        Assert.Equal(JudgementGrade.Perfect, timeline.Judge(note, 3.98));
        Assert.Equal(JudgementGrade.None, timeline.Judge(note, 4.30));
    }

    [Fact]
    public void Un_Multiplicateur_Hors_Contrat_Fait_Echouer_Plutot_Que_De_Silently_Ignorer()
    {
        // Le schema borne w a [0,5 ; 3]. Un chart valide ne peut donc pas
        // contenir 0, mais un chart genere a la main, si. Echouer ici vaut
        // mieux que d'appliquer silencieusement la fenetre de base : le joueur
        // verrait un chart plus facile que ce que le fichier annonce.
        Note horsContrat = Tap(1.0) with { WindowScale = 0 };
        ChartTimeline timeline = Frise(horsContrat);
        Assert.Throws<ArgumentOutOfRangeException>(() => timeline.WindowOf(horsContrat));
    }

    [Fact]
    public void Le_Premier_Index_Jugeable_Exclude_Les_Notes_Dont_La_Fenetre_Est_Fermee()
    {
        // Berceau : fenetre 'bien' de 140 ms. A t = 1,000 s, la note de 1,0 s
        // reste jugable jusqu'a 1,140 s, celle de 0,5 s est fermee depuis 0,64 s.
        ChartTimeline timeline = Frise(Tap(0.5), Tap(1.0), Tap(2.0), Tap(5.0));
        Assert.Equal(1, timeline.FirstJudgableIndex(1.0));
        Assert.Equal(1, timeline.FirstJudgableIndex(1.139));
        Assert.Equal(2, timeline.FirstJudgableIndex(1.141));
    }

    [Fact]
    public void Le_Premier_Index_Jugeable_Est_Zéro_Avant_Le_Debut()
    {
        ChartTimeline timeline = Frise(Tap(0.5), Tap(1.0));
        Assert.Equal(0, timeline.FirstJudgableIndex(0.0));
        Assert.Equal(0, timeline.FirstJudgableIndex(-10.0));
    }

    [Fact]
    public void Le_Premier_Index_Jugeable_Vaut_Le_Nombre_De_Notes_Apres_La_Fin()
    {
        ChartTimeline timeline = Frise(Tap(0.5), Tap(1.0));
        Assert.Equal(2, timeline.FirstJudgableIndex(2.0));
    }

    [Fact]
    public void Le_Premier_Index_Jugeable_Tient_Compte_Du_Multiplicateur_De_La_Note()
    {
        // Deux notes au meme temps, l'une a fenetre triee (w = 0,5) et l'autre
        // large (w = 3). La premiere ferme a 2,070 s, la seconde a 2,420 s :
        // entre les deux, seule la note large doit rester jugeable. C'est le
        // but du champ w, note par note.
        ChartTimeline timeline = Frise(
            Tap(2.0) with { WindowScale = 0.5 },
            Tap(2.0) with { WindowScale = 3 });
        Assert.Equal(0, timeline.FirstJudgableIndex(2.069));
        Assert.Equal(1, timeline.FirstJudgableIndex(2.071));
        Assert.Equal(1, timeline.FirstJudgableIndex(2.419));
        Assert.Equal(2, timeline.FirstJudgableIndex(2.421));
    }

    [Fact]
    public void Visible_Resecte_L_Anticipation()
    {
        ChartTimeline timeline = Frise(Tap(1.0), Tap(1.5), Tap(3.0));
        IReadOnlyList<VisibleNote> visible = timeline.Visible(0.0, 2.0);
        Assert.Equal(2, visible.Count);
        Assert.Equal(1.0, visible[0].Time);
        Assert.Equal(1.5, visible[1].Time);
    }

    [Fact]
    public void Visible_Annonce_Le_Temps_Avant_Arrivee()
    {
        ChartTimeline timeline = Frise(Tap(1.0));
        VisibleNote visible = timeline.Visible(0.75, 1.0)[0];
        Assert.Equal(0.25, visible.DistanceSeconds, 12);
    }

    [Fact]
    public void Une_Note_Dont_La_Fenetre_Est_Fermee_N_Est_Pas_Affichee()
    {
        // On ne reaffiche pas une note ratee : la reaffiche n'apprenant rien au
        // joueur, elle recounted la meme note a chaque image.
        ChartTimeline timeline = Frise(Tap(0.5), Tap(3.0));
        IReadOnlyList<VisibleNote> visible = timeline.Visible(1.0, 5.0);
        Assert.Single(visible);
        Assert.Equal(3.0, visible[0].Time);
    }

    [Fact]
    public void Une_Note_A_Arrivee_Passe_A_Un_Ecart_Negatif()
    {
        ChartTimeline timeline = Frise(Tap(1.0));
        VisibleNote visible = timeline.Visible(1.020, 1.0)[0];
        Assert.Equal(-0.020, visible.DistanceSeconds, 12);
    }

    [Fact]
    public void Une_Note_Simple_N_A_Pas_De_Fin_De_Maintien()
    {
        ChartTimeline timeline = Frise(Tap(1.0));
        Assert.Null(timeline.Visible(0.9, 1.0)[0].HoldEnd);
    }

    [Fact]
    public void Une_Note_Tenue_Expose_Sa_Fin_De_Maintien()
    {
        Note tenue = Note.Hold(1.0, KeyBinding.Grid(4), 0.5);
        ChartTimeline timeline = Frise(tenue);
        VisibleNote visible = timeline.Visible(0.9, 1.0)[0];
        Assert.NotNull(visible.HoldEnd);
        Assert.Equal(1.5, visible.HoldEnd!.Value, 12);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Visible_Refuse_Une_Anticipation_Invalide(double anticipation)
    {
        ChartTimeline timeline = Frise(Tap(1.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => timeline.Visible(0.0, anticipation));
    }

    [Fact]
    public void Une_Anticipation_Nulle_Ne_Montre_Que_Les_Notes_Arrivees()
    {
        ChartTimeline timeline = Frise(Tap(1.0), Tap(1.05));
        Assert.Empty(timeline.Visible(0.9, 0.0));
        Assert.Single(timeline.Visible(1.0, 0.0));
        Assert.Equal(2, timeline.Visible(1.06, 0.0).Count);
    }

    [Fact]
    public void Le_Dernier_Temps_Est_Le_Temps_De_La_Derniere_Note()
    {
        ChartTimeline timeline = Frise(Tap(0.5), Tap(12.25));
        Assert.Equal(12.25, timeline.LastTime);
    }
}
