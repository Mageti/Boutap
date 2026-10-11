// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Boutap.Core.Pack;
using Boutap.Game.Judgement;

namespace Boutap.Game.Session;

/// <summary>Une note, telle que la coque doit la dessiner.</summary>
/// <param name="Index">Index dans la frise, pour suivre la note d'une image a l'autre.</param>
/// <param name="Note">La note.</param>
/// <param name="Time">Instant vise.</param>
/// <param name="DistanceSeconds">Secondes avant l'arrivee ; negatif pour une note depassee.</param>
/// <param name="Window">Fenetre de jugement effective, multiplicateur <c>w</c> compris.</param>
/// <param name="HoldEnd">Fin du maintien, ou <see langword="null"/> pour une note simple.</param>
/// <param name="Judged">
/// Vrai si la note n'attend plus rien du joueur : frappee, manquee, ou tenue
/// terminee. Une tenue en cours de maintien n'est pas jugee, elle est tenue.
/// </param>
/// <param name="Grade">Grade obtenu, ou <see cref="JudgementGrade.None"/>.</param>
public readonly record struct NoteView(
    int Index,
    Note Note,
    double Time,
    double DistanceSeconds,
    JudgementWindow Window,
    double? HoldEnd,
    bool Judged,
    JudgementGrade Grade);

/// <summary>
/// Une partie en cours : la frise, l'arbitrage, le score, et le recit de ce qui
/// s'est passe.
/// </summary>
/// <remarks>
/// <para>
/// La classe ne lit aucune horloge et n'ouvre aucun fichier : on lui donne des
/// instants, elle rend des verdicts. C'est ce qui permet de jouer une partie
/// entiere dans un test, sans son, sans fenetre et sans carte graphique, et de
/// verifier qu'un chart genere est bien jouable avant de le livrer.
/// </para>
/// <para>
/// Le temps passe toujours en secondes absolues sur l'horloge audio. Aucune
/// decision de jeu n'est prise a partir d'une autre horloge : la regle C4 est
/// ce qui fait qu'une partie est reproductible, et un coup juge a partir d'un
/// <c>Stopwatch</c> donnerait un resultat different sur deux machines.
/// </para>
/// </remarks>
public sealed class GameSession
{
    private readonly bool[] _keysDown;
    private readonly int[] _openHolds;
    private readonly JudgementGrade[] _grades;
    private readonly double[] _deltas;
    private readonly bool[] _assisted;
    private readonly List<SessionEvent> _events = [];

    private double _position;
    private bool _started;
    private bool _finished;

    /// <summary>Delai accorde apres la derniere tenue avant de la solder.</summary>
    /// <remarks>
    /// Un joueur qui garde la touche apres la fin du morceau tient la tenue
    /// jusqu'au bout : on ne lui retire pas des points parce que la musique
    /// s'est arretee avant son pouce.
    /// </remarks>
    private const double HoldGraceSeconds = 1.0;

    /// <summary>Delai apres la derniere note avant de declarer la partie finie.</summary>
    private const double TailSeconds = 1.0;

    /// <summary>Construit une partie sur un chart.</summary>
    /// <param name="chart">Le chart, deja valide.</param>
    /// <exception cref="ArgumentNullException"><paramref name="chart"/> est nul.</exception>
    /// <exception cref="ArgumentException">Le chart n'a pas de notes.</exception>
    public GameSession(Chart chart)
    {
        ArgumentNullException.ThrowIfNull(chart);

        if (chart.Notes is not { Count: > 0 })
        {
            throw new ArgumentException(
                "Le chart n'a aucune note : il n'y a pas de partie a jouer.",
                nameof(chart));
        }

        Chart = chart;
        Level = chart.Level ?? ChartLevel.Ronde;
        Timeline = new ChartTimeline(Level, chart.Notes);
        Ledger = new HitLedger(Timeline);
        Stats = new SessionStats();

        _grades = new JudgementGrade[Timeline.Count];
        Array.Fill(_grades, JudgementGrade.None);
        _deltas = new double[Timeline.Count];
        _assisted = new bool[Timeline.Count];
        _keysDown = new bool[KeyMap.SlotCount];
        _openHolds = new int[KeyMap.SlotCount];
        Array.Fill(_openHolds, -1);
    }

    /// <summary>Le chart joue.</summary>
    public Chart Chart { get; }

    /// <summary>Le niveau, donc les fenetres de jugement.</summary>
    public ChartLevel Level { get; }

    /// <summary>La frise des notes.</summary>
    public ChartTimeline Timeline { get; }

    /// <summary>L'etat d'avancement des notes.</summary>
    public HitLedger Ledger { get; }

    /// <summary>Les compteurs de la partie.</summary>
    public SessionStats Stats { get; }

    /// <summary>Vrai si la partie a demarre.</summary>
    public bool IsRunning => _started && !_finished;

    /// <summary>Vrai si la partie est terminee.</summary>
    public bool IsFinished => _finished;

    /// <summary>Dernier instant connu sur l'horloge audio.</summary>
    public double Position => _position;

    /// <summary>Instant vise par la derniere note, tenue comprise.</summary>
    public double EndTime
    {
        get
        {
            double last = Timeline.LastTime;
            foreach (int index in _openHolds)
            {
                if (index >= 0)
                {
                    last = Math.Max(last, Timeline[index].EndTime);
                }
            }

            return last;
        }
    }

    /// <summary>Score rapporte a 1 000 000.</summary>
    public int Score => Stats.NormalizedScore(Timeline.Count);

    /// <summary>Demarre la partie a cet instant.</summary>
    /// <param name="now">Instant de depart, sur l'horloge audio.</param>
    public void Start(double now)
    {
        _position = now;
        _started = true;
    }

    /// <summary>Fait avancer la partie : notes manquees, tenues terminees, fin.</summary>
    /// <param name="now">Instant courant, sur l'horloge audio.</param>
    /// <remarks>
    /// L'ordre compte : on solde d'abord les notes dont la fenetre s'est fermee,
    /// puis les tenues qui ont atteint leur fin. Une tenue n'est jamais
    /// « manquee » : sa note est son arrivee, et son maintien est une autre
    /// decision, prise a la fin.
    /// </remarks>
    public void Update(double now)
    {
        if (!_started || _finished)
        {
            return;
        }

        _position = Math.Max(_position, now);

        ExpireMissed(now);
        SettleHolds(now);

        if (Ledger.IsEmpty && CountOpenHolds() == 0)
        {
            Finish();
        }
    }

    /// <summary>Un coup est porte sur une touche.</summary>
    /// <param name="key">La touche.</param>
    /// <param name="now">Instant du coup, sur l'horloge audio.</param>
    /// <returns>Le verdict de l'arbitrage.</returns>
    /// <remarks>
    /// Une touche deja enfoncee ne rend rien : sans ce garde, un clavier qui
    /// repete l'evenement transformerait un seul geste en rafale, et la meme
    /// note partirait en combo.
    /// </remarks>
    public HitOutcome Press(KeyBinding key, double now)
    {
        if (!IsRunning)
        {
            return HitOutcome.Empty;
        }

        int slot = KeyMap.SlotOf(key);
        if (slot < 0 || _keysDown[slot])
        {
            return HitOutcome.Empty;
        }

        _keysDown[slot] = true;
        _position = Math.Max(_position, now);

        HitOutcome outcome = HitResolver.Resolve(key, now, Timeline, Ledger);
        if (!outcome.HasHit)
        {
            // Un coup dans le vide n'est pas une faute. Il est quand meme
            // signale : la coque doit pouvoir jouer le son d'un coup qui ne
            // trouve rien, sinon le joueur ne sait pas si son geste a ete vu.
            Emit(new SessionEvent(
                SessionEventKind.Miss, JudgementGrade.None, null, now, 0, false, Score, Stats.Combo));
            return outcome;
        }

        Note note = outcome.Note!;
        Ledger.Resolve(outcome.Index);
        _grades[outcome.Index] = outcome.Grade;
        _deltas[outcome.Index] = outcome.Delta;
        _assisted[outcome.Index] = outcome.Assisted;
        Stats.Register(outcome.Grade, outcome.Delta, outcome.Assisted);

        if (note.IsHold)
        {
            _openHolds[slot] = outcome.Index;
        }

        Emit(new SessionEvent(
            SessionEventKind.Hit, outcome.Grade, note, now, outcome.Delta, outcome.Assisted, Score, Stats.Combo));

        return outcome;
    }

    /// <summary>Une touche est relachee.</summary>
    /// <param name="key">La touche.</param>
    /// <param name="now">Instant du relachement, sur l'horloge audio.</param>
    /// <remarks>
    /// <para>
    /// Une tenue se juge a son relachement, comme un coup se juge a son
    /// instant : l'ecart se mesure entre le moment ou le doigt part et la fin
    /// visee, et la meme table de fenetres s'applique. Recuperer ce verdict est
    /// ce qui fait d'une tenue un geste et non un appui maintenu par distraction
    /// — lacher a 80 ms de la fin, c'est tomber de parfait a excellent.
    /// </para>
    /// <para>
    /// Le grade retenu est le plus mauvais des deux : un coup parti de travers
    /// et une tenue lachee n'arrivent pas a se compenser. Lacher une tenue avant
    /// sa fin ne detruit rien d'autre ; la note devient ratee si l'ecart sort de
    /// la fenetre, et rien d'autre ne bouge.
    /// </para>
    /// </remarks>
    public void Release(KeyBinding key, double now)
    {
        if (!IsRunning)
        {
            return;
        }

        int slot = KeyMap.SlotOf(key);
        if (slot < 0 || !_keysDown[slot])
        {
            return;
        }

        _keysDown[slot] = false;
        int index = _openHolds[slot];
        if (index < 0)
        {
            return;
        }

        _openHolds[slot] = -1;
        _position = Math.Max(_position, now);

        Note note = Timeline[index];
        JudgementGrade pressed = _grades[index];

        // Relachee apres la fin visee, la tenue est tenue jusqu'au bout : le
        // joueur a garde la touche, rien a lui reprocher.
        JudgementGrade held = now <= note.EndTime
            ? Timeline.WindowOf(note).Grade(now - note.EndTime)
            : pressed;
        JudgementGrade final = held < pressed ? held : pressed;

        if (final != pressed)
        {
            Stats.Demote(pressed, final, _deltas[index], _assisted[index]);
            _grades[index] = final;
        }

        _deltas[index] = 0;

        Emit(new SessionEvent(
            final == pressed ? SessionEventKind.HoldDone : SessionEventKind.HoldShort,
            final,
            note,
            now,
            note.EndTime - now,
            false,
            Score,
            Stats.Combo));
    }

    /// <summary>Ce qui s'est passe depuis le dernier appel.</summary>
    /// <returns>Les evenements, dans l'ordre. Vide s'il ne s'est rien passe.</returns>
    public IReadOnlyList<SessionEvent> DrainEvents()
    {
        if (_events.Count == 0)
        {
            return [];
        }

        List<SessionEvent> drained = new(_events);
        _events.Clear();
        return drained;
    }

    /// <summary>Ce qu'il faut dessiner maintenant.</summary>
    /// <param name="now">Instant courant, sur l'horloge audio.</param>
    /// <param name="lookaheadSeconds">Secondes de notes visibles a l'avance.</param>
    /// <param name="fadeSeconds">
    /// Secondes pendant lesquelles une note deja jugee reste visible, pour le
    /// fondu. Zero la supprime.
    /// </param>
    /// <returns>Les notes visibles, de la plus vieille a la plus recente.</returns>
    public IReadOnlyList<NoteView> Snapshot(double now, double lookaheadSeconds, double fadeSeconds = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(lookaheadSeconds);
        ArgumentOutOfRangeException.ThrowIfNegative(fadeSeconds);

        List<NoteView> visible = [];
        int start = Timeline.FirstJudgableIndex(now);
        while (start > 0 && Timeline[start - 1].Time >= now - fadeSeconds)
        {
            start--;
        }

        for (int index = start; index < Timeline.Count; index++)
        {
            Note note = Timeline[index];
            if (note.Time - now > lookaheadSeconds)
            {
                break;
            }

            // Une note deja jouee s'efface des que son fondu est passe. Sans ce
            // garde, elle resterait affichee jusqu'a la fermeture de sa fenetre
            // : le joueur verrait trembler sur la ligne une note qu'il a deja
            // frappee, et la viserait une seconde fois.
            if (Ledger.IsResolved(index) && note.Time < now - fadeSeconds)
            {
                continue;
            }

            visible.Add(new NoteView(
                index,
                note,
                note.Time,
                note.Time - now,
                Timeline.WindowOf(note),
                note.IsHold ? note.EndTime : null,
                Ledger.IsResolved(index) && !IsOpenHold(index),
                _grades[index]));
        }

        return visible;
    }

    /// <summary>La premiere note qui attend encore un coup.</summary>
    /// <returns>La vue de la note, ou <see langword="null"/> s'il n'en reste aucune.</returns>
    public NoteView? NextNote(double now)
    {
        for (int index = Timeline.FirstJudgableIndex(now); index < Timeline.Count; index++)
        {
            if (!Ledger.IsResolved(index))
            {
                Note note = Timeline[index];
                return new NoteView(
                    index, note, note.Time, note.Time - now, Timeline.WindowOf(note), null, false, _grades[index]);
            }
        }

        return null;
    }

    /// <summary>Part de la piste deja jouee, de 0 a 1.</summary>
    /// <param name="now">Instant courant.</param>
    /// <returns>La fraction parcourue.</returns>
    public double Progress(double now)
    {
        double end = Math.Max(EndTime, TailSeconds);
        if (end <= 0)
        {
            return 1;
        }

        return Math.Clamp(now / end, 0, 1);
    }

    private void ExpireMissed(double now)
    {
        // Tout ce qui precede cet index a la fenetre fermee. La dichotomie rend
        // le cout independant du nombre de notes deja jouees.
        int firstJudgable = Timeline.FirstJudgableIndex(now);
        for (int index = 0; index < firstJudgable; index++)
        {
            if (Ledger.IsResolved(index))
            {
                continue;
            }

            Ledger.Resolve(index);
            Stats.RegisterMiss();

            Emit(new SessionEvent(
                SessionEventKind.Miss, JudgementGrade.None, Timeline[index], now, 0, false, Score, Stats.Combo));
        }
    }

    private void SettleHolds(double now)
    {
        double limit = Math.Max(EndTime, Timeline.LastTime) + HoldGraceSeconds;
        bool force = now >= limit;

        for (int slot = 0; slot < _openHolds.Length; slot++)
        {
            int index = _openHolds[slot];
            if (index < 0)
            {
                continue;
            }

            if (!force && now < Timeline[index].EndTime)
            {
                continue;
            }

            _openHolds[slot] = -1;
            _keysDown[slot] = false;

            Emit(new SessionEvent(
                SessionEventKind.HoldDone, _grades[index], Timeline[index], now, 0, false, Score, Stats.Combo));
        }
    }

    private int CountOpenHolds()
    {
        int count = 0;
        foreach (int index in _openHolds)
        {
            if (index >= 0)
            {
                count++;
            }
        }

        return count;
    }

    private bool IsOpenHold(int index)
    {
        foreach (int open in _openHolds)
        {
            if (open == index)
            {
                return true;
            }
        }

        return false;
    }

    private void Finish()
    {
        _finished = true;
        Emit(new SessionEvent(
            SessionEventKind.Finished, JudgementGrade.None, null, _position, 0, false, Score, Stats.Combo));
    }

    private void Emit(SessionEvent gameEvent) => _events.Add(gameEvent);
}
