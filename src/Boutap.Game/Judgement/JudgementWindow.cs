// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Boutap.Game.Judgement;

/// <summary>
/// Une fenetre de jugement, en secondes de part et d'autre du temps theorique.
/// </summary>
/// <remarks>
/// Une fenetre est <em>symetrique</em> : jouer 40 ms trop tot et 40 ms trop
/// tard rendent le meme verdict. C'est le choix le moins compliance : un
/// joueur qui vise juste doit etre recompense de la meme facon des deux cotes.
/// </remarks>
/// <param name="Good">Dela au-dela duquel le coup est rate.</param>
/// <param name="Great">Dela au-dela duquel le coup est bon.</param>
/// <param name="Perfect">Dela au-dela duquel le coup est parfait.</param>
public readonly record struct JudgementWindow(double Good, double Great, double Perfect)
{
    /// <summary>Fenetre nulle : tout coup est rate.</summary>
    public static JudgementWindow None => new(0, 0, 0);

    /// <summary>Multiplie les trois cotes par le meme facteur.</summary>
    /// <param name="factor">Facteur strictement positif.</param>
    /// <returns>La fenetre mise a l'echelle.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Le facteur n'est pas positif.</exception>
    public JudgementWindow Scale(double factor)
    {
        if (!(factor > 0) || double.IsInfinity(factor))
        {
            throw new ArgumentOutOfRangeException(
                nameof(factor),
                factor,
                "Un facteur de fenetre doit etre un nombre fini strictement positif.");
        }

        return new JudgementWindow(Good * factor, Great * factor, Perfect * factor);
    }

    /// <summary>Verdict de la fenetre pour un ecart de <paramref name="deltaSeconds"/>.</summary>
    /// <param name="deltaSeconds">Ecart en secondes, signe, mesure sur l'horloge audio.</param>
    /// <returns>Le grade obtenu, <see cref="JudgementGrade.None"/> si rate.</returns>
    public JudgementGrade Grade(double deltaSeconds)
    {
        double magnitude = Math.Abs(deltaSeconds);
        if (magnitude <= Perfect)
        {
            return JudgementGrade.Perfect;
        }

        if (magnitude <= Great)
        {
            return JudgementGrade.Great;
        }

        return magnitude <= Good ? JudgementGrade.Good : JudgementGrade.None;
    }
}
