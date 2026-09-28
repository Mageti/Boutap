// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Boutap.Core.Pack;

namespace Boutap.Game.Judgement;

/// <summary>
/// Les fenetres de jugement de base, par niveau.
/// </summary>
/// <remarks>
/// <para>
/// Ces nombres sont <em>provisoires</em>. La spec ne les fixe nulle part : elle
/// fixe le temps de reaction du joueur (220 ms, 350 ms en berceau, wiki:
/// generateur.md 6.2) mais pas l'exigence du coup. Les valeurs ci-dessous sont
/// un ordre de grandeur usuel, choisi pour rester tres inferieur au temps de
/// reaction : une fenetre large rewarded l'inattention, une fenetre etroite ne
/// pardonne pas la latence de la machine.
/// </para>
/// <para>
/// Elles ne sont pas figees dans le code pour autant : elles vivent dans une
/// seule table, et le test <c>Boutap.Game.Tests</c> verifie que le niveau
/// <c>berceau</c> est plus exigeant en largeur et que <c>cascade</c> est plus
/// strict. Le moment de les remplacer par des valeurs mesurees est note dans
/// le CHANGELOG.
/// </para>
/// <para>
/// Chaque note peut encore reserrer sa propre fenetre par le champ
/// <c>w</c> du format (multiplicateur de 0,5 a 3) : une note rendue difficile
/// a toucher resserre sa cible sans toucher aux autres.
/// </para>
/// </remarks>
public static class JudgementWindows
{
    private static readonly JudgementWindow Berceau = new(Good: 0.140, Great: 0.090, Perfect: 0.045);
    private static readonly JudgementWindow Ronde = new(Good: 0.100, Great: 0.060, Perfect: 0.030);
    private static readonly JudgementWindow Cascade = new(Good: 0.080, Great: 0.045, Perfect: 0.020);

    /// <summary>Fenetre de base du niveau, sans tenir compte du multiplicateur.</summary>
    /// <param name="level">Niveau de difficulte.</param>
    /// <returns>La fenetre de base.</returns>
    public static JudgementWindow ForLevel(ChartLevel level) => level switch
    {
        ChartLevel.Berceau => Berceau,
        ChartLevel.Ronde => Ronde,
        ChartLevel.Cascade => Cascade,
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Niveau de difficulte inconnu."),
    };

    /// <summary>Points de score par grade, pour une partie parfaite a 1 000 000.</summary>
    /// <remarks>
    /// La normalisation a 1 000 000 est imposee par wiki: generateur.md 7.1 :
    /// un morceau a 200 notes ne doit pas valoir plus qu'un morceau a 800.
    /// </remarks>
    /// <param name="grade">Grade attribue.</param>
    /// <returns>Le nombre de points associe.</returns>
    public static int ScoreFor(JudgementGrade grade) => grade switch
    {
        JudgementGrade.Perfect => 1000,
        JudgementGrade.Great => 600,
        JudgementGrade.Good => 250,
        _ => 0,
    };
}
