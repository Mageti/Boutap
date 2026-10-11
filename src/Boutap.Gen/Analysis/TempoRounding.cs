// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Boutap.Gen.Analysis;

/// <summary>
/// Arrondit un tempo mesure a l'unite, et seulement quand la mesure le permet.
/// </summary>
/// <remarks>
/// <para>
/// Le tempo rendu par <see cref="BeatTracker"/> n'est pas un bruit : c'est la
/// moyenne reelle des intervalles detectes, <c>60 * (n - 1) / duree</c>. Un
/// morceau a 120 BPM dont les battements tombent un peu irregulierement donne
/// donc 119,96, et c'est la verite de la mesure.
/// </para>
/// <para>
/// Le ramener a l'entier ne deplace aucune note de plus d'un seizieme de
/// temps, quoi qu'il arrive et quelle que soit la duree du morceau : le
/// quantificateur ancre chaque onset sur le battement detecte le plus proche
/// et recalcule la grille a chaque temps, il n'extrapole jamais une grille
/// depuis un tempo. Arrondir n'est donc pas une correction de derive, c'est
/// une question d'affichage.
/// </para>
/// <para>
/// La tolerancce est deliberement serree. Arrondir un tempo de 119,96 a 120
/// coute 0,04 BPM ; arrondir un tempo de 120,24 a 120 en couterait 0,24, soit
/// dix fois plus pour un gain nul. Au-dela de <see cref="ToleranceBpm"/>, le
/// tempo mesure est conserve tel quel : un morceau a 137,3 BPM existe, et
/// l'arrondir mentirait sur lui.
/// </para>
/// </remarks>
public static class TempoRounding
{
    /// <summary>
    /// Ecart maximal, en BPM, en deca duquel un tempo mesure est ramene a
    /// l'entier. Un dixieme de BPM, soit l'ordre de la precision de la mesure
    /// sur un morceau de quelques minutes.
    /// </summary>
    public const double ToleranceBpm = 0.10;

    /// <summary>Le tempo a afficher : l'entier proche, ou la mesure.</summary>
    /// <param name="measuredBpm">Tempo mesure, en battements par minute.</param>
    /// <returns>L'entier le plus proche si la mesure y ressemble, sinon la mesure.</returns>
    public static double Display(double measuredBpm)
    {
        if (!double.IsFinite(measuredBpm) || measuredBpm <= 0)
        {
            return measuredBpm;
        }

        return IsRounded(measuredBpm)
            ? Math.Round(measuredBpm, MidpointRounding.AwayFromZero)
            : measuredBpm;
    }

    /// <summary>Vrai si <see cref="Display"/> ramene le tempo a un entier.</summary>
    /// <param name="measuredBpm">Tempo mesure, en battements par minute.</param>
    /// <returns><see langword="true"/> si l'affichage differe de la mesure.</returns>
    public static bool IsRounded(double measuredBpm)
    {
        if (!double.IsFinite(measuredBpm) || measuredBpm <= 0)
        {
            return false;
        }

        return Math.Abs(measuredBpm - Math.Round(measuredBpm, MidpointRounding.AwayFromZero))
            <= ToleranceBpm;
    }
}
