// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Boutap.Game.Judgement;

/// <summary>Verdict d'un coup joue, du plus ouvert au plus exige.</summary>
/// <remarks>
/// L'ordre de declaration est l'ordre de l'echelle : comparer deux
/// <see cref="JudgementGrade"/> revient donc a comparer la qualite.
/// </remarks>
public enum JudgementGrade
{
    /// <summary>Rien : trop tot, trop tard, ou jamais.</summary>
    None = 0,

    /// <summary>Coup correct, dans la fenetre la plus large.</summary>
    Good = 1,

    /// <summary>Coup bien place.</summary>
    Great = 2,

    /// <summary>Coup dans la fenetre etroite.</summary>
    Perfect = 3,
}
