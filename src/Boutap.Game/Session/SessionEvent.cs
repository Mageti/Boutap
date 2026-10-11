// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Boutap.Core.Pack;
using Boutap.Game.Judgement;

namespace Boutap.Game.Session;

/// <summary>Ce qui s'est passe, dans l'ordre, pour que la coque le rende.</summary>
/// <remarks>
/// La coque ne devine rien et ne recalcule rien : elle vide cette file et joue
/// le son, l'image et le texte correspondants. Un evenement par evenement, la
/// vue reste une vue.
/// </remarks>
public enum SessionEventKind
{
    /// <summary>Un coup a touche une note.</summary>
    Hit,

    /// <summary>Une note est passee sans coup.</summary>
    Miss,

    /// <summary>Une tenue a ete lachee avant sa fin.</summary>
    HoldShort,

    /// <summary>Une tenue est allee a son terme.</summary>
    HoldDone,

    /// <summary>La partie est terminee.</summary>
    Finished,
}

/// <summary>Un fait de jeu, horodate par l'instant ou il s'est produit.</summary>
/// <param name="Kind">La nature du fait.</param>
/// <param name="Grade">Le grade, pour un coup ou une tenue corrigee.</param>
/// <param name="Note">La note concernee, ou <see langword="null"/> pour la fin de partie.</param>
/// <param name="Time">Instant du fait, sur l'horloge audio.</param>
/// <param name="Delta">Ecart du geste, en secondes. Nul quand il n'y a pas de geste.</param>
/// <param name="Assisted">Vrai si l'aide au geste a servi.</param>
/// <param name="Score">Score normalise apres le fait.</param>
/// <param name="Combo">Combo apres le fait.</param>
public readonly record struct SessionEvent(
    SessionEventKind Kind,
    JudgementGrade Grade,
    Note? Note,
    double Time,
    double Delta,
    bool Assisted,
    int Score,
    int Combo);
