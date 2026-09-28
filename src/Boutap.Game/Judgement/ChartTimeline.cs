// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Boutap.Core.Pack;

namespace Boutap.Game.Judgement;

/// <summary>Une note et ce que le joueur doit voir d'elle a un instant donne.</summary>
/// <param name="Note">La note elle-meme.</param>
/// <param name="Time">Temps d'arrivee, en secondes sur l'horloge audio.</param>
/// <param name="Window">Fenetre de jugement effective, multiplicateur <c>w</c> compris.</param>
/// <param name="HoldEnd">Fin du maintien, ou <see langword="null"/> pour une note simple.</param>
/// <param name="DistanceSeconds">Secondes avant l'arrivee ; negatif une fois frappe.</param>
public readonly record struct VisibleNote(
    Note Note,
    double Time,
    JudgementWindow Window,
    double? HoldEnd,
    double DistanceSeconds);

/// <summary>
/// La lecture d'un chart : reponse aux questions « qu'est-ce que je vois » et
/// « ce coup vaut quoi », sans aucune dependance au moteur.
/// </summary>
/// <remarks>
/// <para>
/// La classe ne lit aucune horloge et n'alloue pas dans une boucle d'image :
/// elle ne fait que repondre a des questions. C'est ce qui permet de la
/// tester sans fenetre, sans carte graphique et sans son.
/// </para>
/// <para>
/// Les notes doivent etre triees par temps croissant, ce que le validateur de
/// packs garantit deja (<c>chart.notes-unsorted</c>). Le constructeur refuse
/// donc une liste non triee plutot que de la trier en silence : un chart
/// desordonne est un bug de generation, pas un cas a rattraper.
/// </para>
/// </remarks>
public sealed class ChartTimeline
{
    private readonly Note[] _notes;
    private readonly JudgementWindow _base;

    /// <summary>Construit une frise.</summary>
    /// <param name="level">Niveau, pour les fenetres de base.</param>
    /// <param name="notes">Notes triees par temps croissant.</param>
    /// <exception cref="ArgumentNullException"><paramref name="notes"/> est nul.</exception>
    /// <exception cref="ArgumentException">Les notes ne sont pas triees.</exception>
    public ChartTimeline(ChartLevel level, IReadOnlyList<Note> notes)
    {
        ArgumentNullException.ThrowIfNull(notes);
        _notes = new Note[notes.Count];
        _base = JudgementWindows.ForLevel(level);
        for (int index = 0; index < notes.Count; index++)
        {
            Note note = notes[index];
            if (index > 0 && note.Time < _notes[index - 1].Time)
            {
                throw new ArgumentException(
                    $"Les notes doivent etre triees par temps croissant ; la note d'index {index} "
                    + $"est a {note.Time} s alors que la precedente est a {_notes[index - 1].Time} s.",
                    nameof(notes));
            }

            _notes[index] = note;
        }
    }

    /// <summary>Nombre de notes de la frise.</summary>
    public int Count => _notes.Length;

    /// <summary>Temps de la derniere note, ou 0 si la frise est vide.</summary>
    public double LastTime => _notes.Length == 0 ? 0 : _notes[^1].Time;

    /// <summary>Fenetre de base du niveau, avant multiplicateur.</summary>
    public JudgementWindow BaseWindow => _base;

    /// <summary>Accede a une note par son index.</summary>
    /// <param name="index">Index, de 0 a <see cref="Count"/> exclu.</param>
    /// <returns>La note.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Index hors bornes.</exception>
    public Note this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _notes.Length);
            return _notes[index];
        }
    }

    /// <summary>Fenetre reelle d'une note, multiplicateur <c>w</c> compris.</summary>
    /// <param name="note">La note.</param>
    /// <returns>La fenetre effective.</returns>
    public JudgementWindow WindowOf(Note note) => _base.Scale(note.WindowScaleOrDefault);

    /// <summary>Verdict d'un coup sur une note.</summary>
    /// <param name="note">La note visee.</param>
    /// <param name="hitTime">Instant du coup, en secondes sur l'horloge audio.</param>
    /// <returns>Le grade, <see cref="JudgementGrade.None"/> si le coup est trop eloigne.</returns>
    public JudgementGrade Judge(Note note, double hitTime)
        => WindowOf(note).Grade(hitTime - note.Time);

    /// <summary>Premier index dont la fenetre n'est pas encore fermee.</summary>
    /// <remarks>
    /// C'est la borne de la boucle de jeu : aucune note avant cet index ne peut
    /// encore etre frappee, donc aucune note avant cet index ne doit etre
    /// recherchee. La recherche est dichotomique, donc le cout est independant
    /// du nombre de notes.
    /// </remarks>
    /// <param name="time">Instant courant, en secondes.</param>
    /// <returns>Index dans [0, <see cref="Count"/>].</returns>
    public int FirstJudgableIndex(double time)
    {
        int low = 0;
        int high = _notes.Length;
        while (low < high)
        {
            int middle = low + ((high - low) / 2);
            Note note = _notes[middle];
            // La fenetre est symetrique autour de l'arrivee : une note reste
            // jugable jusqu'a T + Good, pas a partir de T - Good. Chercher la
            // bornes par l'arriere de la note ferait disparaitre du tableau les
            // notes que le joueur peut encore frapper.
            double close = note.Time + WindowOf(note).Good;
            if (close >= time)
            {
                high = middle;
            }
            else
            {
                low = middle + 1;
            }
        }

        return low;
    }

    /// <summary>Notes visibles a un instant, pour l'affichage.</summary>
    /// <param name="time">Instant courant, en secondes sur l'horloge audio.</param>
    /// <param name="lookaheadSeconds">Secondes a afficher en avance.</param>
    /// <returns>Une liste ordonnee, reutilisable d'un appel a l'autre.</returns>
    /// <exception cref="ArgumentOutOfRangeException">L'anticipation est negative ou infinie.</exception>
    public IReadOnlyList<VisibleNote> Visible(double time, double lookaheadSeconds)
    {
        if (!(lookaheadSeconds >= 0) || double.IsInfinity(lookaheadSeconds))
        {
            throw new ArgumentOutOfRangeException(
                nameof(lookaheadSeconds),
                lookaheadSeconds,
                "L'anticipation doit etre un nombre fini positif ou nul.");
        }

        List<VisibleNote> visible = [];
        // On part de la premiere note encore jugable : une note plus ancienne a
        // deja eu sa chance, la reafficher n'apprendrait rien au joueur et
        // recountait la meme note a chaque image.
        int start = FirstJudgableIndex(time);
        for (int index = start; index < _notes.Length; index++)
        {
            Note note = _notes[index];
            if (note.Time - time > lookaheadSeconds)
            {
                break;
            }

            visible.Add(new VisibleNote(
                note,
                note.Time,
                WindowOf(note),
                note.IsHold ? note.EndTime : null,
                note.Time - time));
        }

        return visible;
    }
}
