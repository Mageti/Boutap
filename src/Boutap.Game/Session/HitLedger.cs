// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Boutap.Core.Pack;
using Boutap.Game.Judgement;

namespace Boutap.Game.Session;

/// <summary>
/// L'etat d'avancement de la partie vu du chart : quelles notes restent a
/// jouer.
/// </summary>
/// <remarks>
/// <para>
/// Une note est dite <em>resolue</em> quand elle ne peut plus etre frappee,
/// soit parce qu'un coup l'a touchee, soit parce que sa fenetre est fermee. Le
/// registre ne decide rien : il sait seulement ce qui est deja joue, et ou
/// commencer a chercher. C'est <see cref="HitResolver"/> qui arbitre, et
/// <see cref="GameSession"/> qui decide du temps.
/// </para>
/// <para>
/// Le compteur de notes en attente evite de balayer la frise entiere pour
/// repondre a « est-ce fini » dans la boucle de jeu.
/// </para>
/// </remarks>
public sealed class HitLedger
{
    private readonly bool[] _resolved;
    private int _pending;

    /// <summary>Construit le registre d'une frise.</summary>
    /// <param name="timeline">La frise, deja validee.</param>
    /// <exception cref="ArgumentNullException"><paramref name="timeline"/> est nulle.</exception>
    public HitLedger(ChartTimeline timeline)
    {
        ArgumentNullException.ThrowIfNull(timeline);

        Timeline = timeline;
        _resolved = new bool[timeline.Count];
        _pending = timeline.Count;
    }

    /// <summary>Nombre de notes de la frise.</summary>
    public int Count => _resolved.Length;

    /// <summary>Vrai si plus aucune note n'attend de coup.</summary>
    public bool IsEmpty => _pending == 0;

    /// <summary>Vrai si la note a cet index est deja jouee ou manquee.</summary>
    /// <param name="index">Index dans la frise.</param>
    /// <returns>Vrai si elle est resolue.</returns>
    public bool IsResolved(int index) => _resolved[index];

    /// <summary>Marque la note comme resolue.</summary>
    /// <param name="index">Index dans la frise.</param>
    /// <remarks>
    /// Appeler deux fois sur le meme index ne des incremente pas le compteur :
    /// une note est resolue ou elle ne l'est pas, et un double appel serait un
    /// bug de l'appelant, pas un etat a representer.
    /// </remarks>
    public void Resolve(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _resolved.Length);

        if (_resolved[index])
        {
            return;
        }

        _resolved[index] = true;
        _pending--;
    }

    /// <summary>Premier index dont la fenetre n'est pas encore fermee.</summary>
    /// <param name="time">Instant courant.</param>
    /// <returns>Index dans [0 ; <see cref="Count"/>].</returns>
    public int FirstJudgable(double time) => Timeline.FirstJudgableIndex(time);

    /// <summary>La frise que ce registre decrit.</summary>
    public ChartTimeline Timeline { get; }

    /// <summary>Vrai si la fenetre de la note est fermee a cet instant.</summary>
    /// <param name="index">Index dans la frise.</param>
    /// <param name="time">Instant courant.</param>
    /// <returns>Vrai si le coup arrive trop tard.</returns>
    public bool IsExpired(int index, double time)
    {
        Note note = Timeline[index];
        return time > note.Time + Timeline.WindowOf(note).Good;
    }
}
