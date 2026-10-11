// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Boutap.Core.Pack;
using Boutap.Game.Judgement;
using Boutap.Game.Session;
using Xunit;

namespace Boutap.Game.Tests.Session;

/// <summary>Fabrique un chart de test, en une ligne.</summary>
internal static class Charts
{
    internal static Chart OfNotes(ChartLevel level, params Note[] notes) => new()
    {
        Schema = "boutap/chart/1",
        Level = level,
        Notes = notes,
    };
}

public class GameSessionTests
{
    private const double Start = 0;

    // Les fenetres de ronde : Good 0,100 ; Great 0,060 ; Perfect 0,030.
    private static readonly JudgementWindow RondeWindow = JudgementWindows.ForLevel(ChartLevel.Ronde);

    private static (GameSession Session, IReadOnlyList<SessionEvent> Events) Play(
        Chart chart,
        double start = Start,
        double step = 0.01)
    {
        GameSession session = new(chart);
        session.Start(start);
        double end = session.EndTime + 0.5;
        for (double now = start; now <= end; now += step)
        {
            session.Update(now);
        }

        return (session, session.DrainEvents());
    }

    private static int CountGrade(IReadOnlyList<SessionEvent> events, JudgementGrade grade)
    {
        int count = 0;
        foreach (SessionEvent gameEvent in events)
        {
            if (gameEvent.Kind == SessionEventKind.Hit && gameEvent.Grade == grade)
            {
                count++;
            }
        }

        return count;
    }

    [Fact]
    public void UnCoupJusteEstParfait()
    {
        GameSession session = new(Charts.OfNotes(ChartLevel.Ronde, Note.Tap(1.0, KeyBinding.Grid(4))));
        session.Start(Start);

        HitOutcome outcome = session.Press(KeyBinding.Grid(4), 1.0);

        Assert.Equal(JudgementGrade.Perfect, outcome.Grade);
        Assert.Equal(1, session.Stats.Perfect);
    }

    [Fact]
    public void UnCoupDansLaFenetreGrandEstJuge()
    {
        GameSession session = new(Charts.OfNotes(ChartLevel.Ronde, Note.Tap(1.0, KeyBinding.Grid(4))));
        session.Start(Start);

        // Volontairement en retrait du seuil, et non pile dessus. En virgule
        // flottante, « 1,0 + 0,060 » ne redonne pas 0,060 a la soustraction :
        // 1,06 - 1,0 vaut 0,06000000000000005, et le coup franchement juste
        // serait declare hors fenetre. Un seuil se teste par le dedans, et la
        // borne exacte est couverte par le coup pile sur la note.
        HitOutcome outcome = session.Press(KeyBinding.Grid(4), 1.0 + RondeWindow.Great - 0.001);

        Assert.Equal(JudgementGrade.Great, outcome.Grade);
    }

    [Fact]
    public void UnCoupHorsFenetreNeToucheRien()
    {
        GameSession session = new(Charts.OfNotes(ChartLevel.Ronde, Note.Tap(1.0, KeyBinding.Grid(4))));
        session.Start(Start);

        HitOutcome outcome = session.Press(KeyBinding.Grid(4), 1.0 + RondeWindow.Good + 0.01);

        Assert.False(outcome.HasHit);
        Assert.Equal(JudgementGrade.None, outcome.Grade);
    }

    [Fact]
    public void UneNoteSansCoupEstManqueeEtCoupeLeCombo()
    {
        (GameSession session, IReadOnlyList<SessionEvent> events) = Play(
            Charts.OfNotes(ChartLevel.Ronde, Note.Tap(0.5, KeyBinding.Grid(4))));

        Assert.Equal(0, session.Stats.Judged);
        Assert.Equal(1, session.Stats.Missed);
        Assert.Equal(0, session.Stats.Combo);
        Assert.Equal(0, session.Score);
        Assert.Contains(events, gameEvent => gameEvent.Kind == SessionEventKind.Miss && gameEvent.Note is not null);
    }

    [Fact]
    public void UnCoupDansLeVideNeCasseRienEtEstSignale()
    {
        GameSession session = new(Charts.OfNotes(ChartLevel.Ronde, Note.Tap(1.0, KeyBinding.Grid(0))));
        session.Start(Start);

        HitOutcome outcome = session.Press(KeyBinding.Grid(8), 1.0);

        Assert.False(outcome.HasHit);
        IReadOnlyList<SessionEvent> events = session.DrainEvents();
        Assert.Single(events);
        Assert.Equal(SessionEventKind.Miss, events[0].Kind);
        Assert.Null(events[0].Note);
    }

    [Fact]
    public void UneToucheVoisineSecourtLeDoigtQuiGlisse()
    {
        GameSession session = new(Charts.OfNotes(ChartLevel.Ronde, Note.Tap(1.0, KeyBinding.Grid(4))));
        session.Start(Start);

        // La touche 1 est au-dessus de la touche 4 : une case de decalage.
        HitOutcome outcome = session.Press(KeyBinding.Grid(1), 1.0);

        Assert.True(outcome.HasHit);
        Assert.True(outcome.Assisted);
        Assert.Equal(JudgementGrade.Perfect, outcome.Grade);
        Assert.Equal(1, session.Stats.Assisted);
    }

    [Fact]
    public void LAideAuGesteNePrendJamaisLaNoteDeLaBonneTouche()
    {
        GameSession session = new(Charts.OfNotes(
            ChartLevel.Ronde,
            Note.Tap(1.0, KeyBinding.Grid(4)),
            Note.Tap(1.0, KeyBinding.Grid(5))));
        session.Start(Start);

        // La touche 8 est voisine de la 5, pas de la 4. La note de la touche 4
        // doit rester disponible : c'est le seul cas ou la regle « exact
        // d'abord » protege le joueur.
        HitOutcome outcome = session.Press(KeyBinding.Grid(8), 1.0);

        Assert.True(outcome.HasHit);
        Assert.Equal(KeyBinding.Grid(5), outcome.Note!.Key);

        HitOutcome second = session.Press(KeyBinding.Grid(4), 1.0);
        Assert.False(second.Assisted);
        Assert.Equal(KeyBinding.Grid(4), second.Note!.Key);
    }

    [Fact]
    public void LAideAuGesteIgnoreLaDiagonale()
    {
        GameSession session = new(Charts.OfNotes(ChartLevel.Ronde, Note.Tap(1.0, KeyBinding.Grid(4))));
        session.Start(Start);

        // 0 est en diagonale de 4 : deux cases d'erreur, pas une.
        HitOutcome outcome = session.Press(KeyBinding.Grid(0), 1.0);

        Assert.False(outcome.HasHit);
    }

    [Fact]
    public void UneSecondePressionSurLaMemeToucheNeComptePas()
    {
        GameSession session = new(Charts.OfNotes(
            ChartLevel.Ronde,
            Note.Tap(1.0, KeyBinding.Grid(4)),
            Note.Tap(1.2, KeyBinding.Grid(4))));
        session.Start(Start);

        HitOutcome first = session.Press(KeyBinding.Grid(4), 1.0);
        HitOutcome ignored = session.Press(KeyBinding.Grid(4), 1.0);

        Assert.True(first.HasHit);
        Assert.False(ignored.HasHit);
        Assert.Equal(1, session.Stats.Judged);
    }

    [Fact]
    public void UnePartieParfaiteVautUnMillion()
    {
        Chart chart = Charts.OfNotes(
            ChartLevel.Ronde,
            Note.Tap(0.5, KeyBinding.Grid(0)),
            Note.Tap(0.9, KeyBinding.Grid(1)),
            Note.Tap(1.3, KeyBinding.Grid(8)));

        GameSession session = new(chart);
        session.Start(Start);

        foreach (Note note in chart.Notes!)
        {
            session.Press(note.Key, note.Time);
            session.Update(note.Time + 0.01);
        }

        session.Update(session.EndTime + 1.0);

        Assert.True(session.IsFinished);
        Assert.Equal(1_000_000, session.Score);
        Assert.Equal(3, session.Stats.MaxCombo);
        Assert.Equal(3, session.Stats.Perfect);
    }

    [Fact]
    public void LeScoreNeDependPasDuNombreDeNotes()
    {
        // Un coup parfait sur un morceau d'une note, et un coup parfait sur un
        // morceau de cinq : le plafond est le meme. C'est le sens de la
        // normalisation, et la raison pour laquelle « 200 000 points » ne veut
        // rien dire sans connaitre le nombre de notes.
        Chart one = Charts.OfNotes(ChartLevel.Ronde, Note.Tap(0.5, KeyBinding.Grid(0)));
        Chart five = Charts.OfNotes(
            ChartLevel.Ronde,
            Note.Tap(0.5, KeyBinding.Grid(0)),
            Note.Tap(0.7, KeyBinding.Grid(1)),
            Note.Tap(0.9, KeyBinding.Grid(2)),
            Note.Tap(1.1, KeyBinding.Grid(3)),
            Note.Tap(1.3, KeyBinding.Grid(4)));

        GameSession single = new(one);
        single.Start(Start);
        single.Press(KeyBinding.Grid(0), 0.5);
        single.Update(single.EndTime + 1.0);

        GameSession several = new(five);
        several.Start(Start);
        foreach (Note note in five.Notes!)
        {
            several.Press(note.Key, note.Time);
            several.Update(note.Time + 0.01);
        }

        several.Update(several.EndTime + 1.0);

        Assert.Equal(1_000_000, single.Score);
        Assert.Equal(1_000_000, several.Score);
    }

    [Fact]
    public void UnCompteASuivreManqueEtCasseLeCombo()
    {
        GameSession session = new(Charts.OfNotes(
            ChartLevel.Ronde,
            Note.Tap(0.5, KeyBinding.Grid(0)),
            Note.Tap(0.9, KeyBinding.Grid(1))));
        session.Start(Start);

        session.Press(KeyBinding.Grid(0), 0.5);
        session.Update(0.6);
        Assert.Equal(1, session.Stats.Combo);

        // La seconde note n'est jamais frappee : sa fenetre se ferme.
        session.Update(1.1);

        Assert.Equal(0, session.Stats.Combo);
        Assert.Equal(1, session.Stats.Missed);
    }

    [Fact]
    public void UneTenueTenueJusquAuBoutGardeSonGrade()
    {
        GameSession session = new(Charts.OfNotes(
            ChartLevel.Ronde,
            Note.Hold(0.5, KeyBinding.Grid(4), 1.0)));
        session.Start(Start);

        session.Press(KeyBinding.Grid(4), 0.5);
        Assert.Equal(1, session.Stats.Perfect);

        // Relachee exactement a la fin visee, a la milliseconde pres.
        session.Release(KeyBinding.Grid(4), 1.5);

        Assert.Equal(1, session.Stats.Perfect);
        Assert.Equal(0, session.Stats.Good + session.Stats.Great);
    }

    [Fact]
    public void UneTenueLacheeQuaranteCinqMillisecondesAvantLaFinTombeDeGrand()
    {
        GameSession session = new(Charts.OfNotes(
            ChartLevel.Ronde,
            Note.Hold(0.5, KeyBinding.Grid(4), 1.0)));
        session.Start(Start);

        session.Press(KeyBinding.Grid(4), 0.5);
        session.Release(KeyBinding.Grid(4), 1.5 - 0.045);

        IReadOnlyList<SessionEvent> events = session.DrainEvents();

        // 45 ms avant la fin visee : dans la fenetre « excellent » (60 ms), hors
        // de celle de « parfait » (30 ms). La meme table de fenetres que pour un
        // coup, retournee : ici c'est le depart du doigt qui est juge.
        Assert.Contains(events, gameEvent => gameEvent.Kind == SessionEventKind.HoldShort);
        Assert.Contains(events, gameEvent => gameEvent.Grade == JudgementGrade.Great && gameEvent.Note!.IsHold);
        Assert.Equal(1, session.Stats.Great);
        Assert.Equal(0, session.Stats.Perfect);
    }

    [Fact]
    public void UneTenueLacheeVingtMillisecondesAvantLaFinResteParfaite()
    {
        GameSession session = new(Charts.OfNotes(
            ChartLevel.Ronde,
            Note.Hold(0.5, KeyBinding.Grid(4), 1.0)));
        session.Start(Start);

        // Vingt millisecondes, c'est encore dans les trente de la fenetre
        // parfaite. Ce test existe parce que la premiere version de son voisin
        // placait la borne a vingt millisecondes aussi : c'etait la borne qui
        // etait fausse, pas le mecanisme. Le garder dit ou elle est vraiment.
        session.Press(KeyBinding.Grid(4), 0.5);
        session.Release(KeyBinding.Grid(4), 1.5 - 0.02);

        Assert.Equal(1, session.Stats.Perfect);
        Assert.Equal(0, session.Stats.Great + session.Stats.Good);
    }

    [Fact]
    public void UneTenueLacheeTresTotNeComptePlusCommeJouee()
    {
        GameSession session = new(Charts.OfNotes(
            ChartLevel.Ronde,
            Note.Hold(0.5, KeyBinding.Grid(4), 1.0)));
        session.Start(Start);

        // Le grade parfait tombe a None : la note quitte les coups comptes et
        // rejoint les manquees, sinon le denominateur du score se deplacerait.
        session.Press(KeyBinding.Grid(4), 0.5);
        session.Release(KeyBinding.Grid(4), 0.55);

        Assert.Equal(0, session.Stats.Perfect + session.Stats.Great + session.Stats.Good);
        Assert.Equal(0, session.Stats.Judged);
        Assert.Equal(0, session.Stats.RawScore);
    }

    [Fact]
    public void UnePartieSansAucunCoupSeTermineQuandMeme()
    {
        (GameSession session, _) = Play(
            Charts.OfNotes(ChartLevel.Ronde, Note.Tap(0.2, KeyBinding.Grid(3)), Note.Tap(0.4, KeyBinding.Grid(5))));

        // C1 : rater tout ne met pas fin a la partie plus tot. Elle se termine
        // quand le morceau est fini, pas quand le joueur a echoue.
        Assert.True(session.IsFinished);
        Assert.Equal(2, session.Stats.Missed);
        Assert.Equal(0, session.Score);
    }

    [Fact]
    public void LaPartieSeTermineApresLaDerniereNote()
    {
        (GameSession session, IReadOnlyList<SessionEvent> events) = Play(
            Charts.OfNotes(ChartLevel.Ronde, Note.Tap(0.2, KeyBinding.Grid(3))));

        Assert.True(session.IsFinished);
        Assert.Contains(events, gameEvent => gameEvent.Kind == SessionEventKind.Finished);

        // Un coup apres la fin ne compte plus.
        HitOutcome late = session.Press(KeyBinding.Grid(3), 10.0);
        Assert.False(late.HasHit);
    }

    [Fact]
    public void LesFenetresSuiventLeNiveau()
    {
        Chart berceau = Charts.OfNotes(ChartLevel.Berceau, Note.Tap(1.0, KeyBinding.Grid(4)));
        Chart cascade = Charts.OfNotes(ChartLevel.Cascade, Note.Tap(1.0, KeyBinding.Grid(4)));

        GameSession easy = new(berceau);
        easy.Start(Start);

        GameSession hard = new(cascade);
        hard.Start(Start);

        // Le meme ecart de 100 ms passe en berceau et rate en cascade : c'est
        // la seule difference entre les deux, et elle doit etre reelle.
        HitOutcome onEasy = easy.Press(KeyBinding.Grid(4), 1.10);
        HitOutcome onHard = hard.Press(KeyBinding.Grid(4), 1.10);

        Assert.True(onEasy.HasHit);
        Assert.False(onHard.HasHit);
    }

    [Fact]
    public void LeMultiplicateurDeFenetreDuneNoteCompte()
    {
        GameSession session = new(Charts.OfNotes(
            ChartLevel.Ronde,
            Note.Tap(1.0, KeyBinding.Grid(4)) with { WindowScale = 3.0 }));
        session.Start(Start);

        HitOutcome outcome = session.Press(KeyBinding.Grid(4), 1.0 + (RondeWindow.Good * 2));

        Assert.True(outcome.HasHit);
    }

    [Fact]
    public void LesVuesSuiventLAvancement()
    {
        GameSession session = new(Charts.OfNotes(
            ChartLevel.Ronde,
            Note.Tap(0.5, KeyBinding.Grid(0)),
            Note.Tap(1.0, KeyBinding.Grid(1))));
        session.Start(Start);

        session.Press(KeyBinding.Grid(0), 0.5);
        IReadOnlyList<NoteView> views = session.Snapshot(0.6, lookaheadSeconds: 1.0, fadeSeconds: 1.0);

        Assert.Contains(views, view => view.Index == 0 && view.Judged);
        Assert.Contains(views, view => view.Index == 1 && !view.Judged);
    }

    [Fact]
    public void UneNoteDejaJugeeNeRevientPasDansLesVues()
    {
        GameSession session = new(Charts.OfNotes(ChartLevel.Ronde, Note.Tap(0.5, KeyBinding.Grid(0))));
        session.Start(Start);

        session.Press(KeyBinding.Grid(0), 0.5);
        IReadOnlyList<NoteView> withoutFade = session.Snapshot(0.6, lookaheadSeconds: 1.0);

        Assert.DoesNotContain(withoutFade, view => view.Index == 0);
    }

    [Fact]
    public void UnChartSansNoteEstRefuse()
    {
        ArgumentException raised = Assert.Throws<ArgumentException>(
            () => new GameSession(new Chart { Level = ChartLevel.Ronde }));

        Assert.Contains("aucune note", raised.Message);
    }
}
