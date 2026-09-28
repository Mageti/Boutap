// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Boutap.Core.Pack;
using Boutap.Game.Judgement;
using Xunit;

namespace Boutap.Game.Tests.Judgement;

/// <summary>
/// Les fenetres de jugement. Le point non negociable est la symetrie : jouer
/// 40 ms trop tot et 40 ms trop tard doivent rendre le meme verdict.
/// </summary>
public sealed class JudgementWindowTests
{
    private static readonly JudgementWindow Subject = new(Good: 0.100, Great: 0.060, Perfect: 0.030);

    [Theory]
    [InlineData(0.0, JudgementGrade.Perfect)]
    [InlineData(0.030, JudgementGrade.Perfect)]
    [InlineData(-0.030, JudgementGrade.Perfect)]
    [InlineData(0.0301, JudgementGrade.Great)]
    [InlineData(-0.0301, JudgementGrade.Great)]
    [InlineData(0.060, JudgementGrade.Great)]
    [InlineData(0.0601, JudgementGrade.Good)]
    [InlineData(0.100, JudgementGrade.Good)]
    [InlineData(0.1001, JudgementGrade.None)]
    [InlineData(-100.0, JudgementGrade.None)]
    public void Grade_Classifie_L_Ecart(double delta, JudgementGrade attendu)
    {
        Assert.Equal(attendu, Subject.Grade(delta));
    }

    [Fact]
    public void Grade_Est_Symetrique()
    {
        for (int millisecondes = 0; millisecondes <= 120; millisecondes++)
        {
            double delta = millisecondes / 1000.0;
            Assert.Equal(Subject.Grade(delta), Subject.Grade(-delta));
        }
    }

    [Fact]
    public void Grade_Est_Degrade_Quand_La_Fenetre_Se_Retrecit()
    {
        // Un multiplicateur de 0,5 (borne basse du format) doit rendre la note
        // plus difficile : c'est le levier que le generateur utilise pour
        // resserrer un passage sans toucher aux fenetres de base.
        JudgementWindow serree = Subject.Scale(0.5);
        Assert.Equal(JudgementGrade.Great, Subject.Grade(0.040));
        Assert.Equal(JudgementGrade.Good, serree.Grade(0.040));
        Assert.Equal(JudgementGrade.None, serree.Scale(0.5).Grade(0.040));
    }

    [Fact]
    public void Scale_Multiplie_Les_Trois_Cotes()
    {
        JudgementWindow large = Subject.Scale(3);
        Assert.Equal(0.300, large.Good, 12);
        Assert.Equal(0.180, large.Great, 12);
        Assert.Equal(0.090, large.Perfect, 12);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Scale_Refuse_Une_Echelle_Invalide(double facteur)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Subject.Scale(facteur));
    }

    [Fact]
    public void Une_Fenetre_Nulle_Recuse_Tout_Coup_Exception_L_Instant_Exact()
    {
        // Cas limite documente plutot que corrige : les bornes sont testees
        // par <=, donc un coup tombe exactement sur l'arrivee reste parfait,
        // meme sans aucune marge. Une fenetre nulle n'apparait pas dans un pack
        // valide (w est borne a [0,5 ; 3]) ; la fixer ici evite surtout qu'un
        // refactor ne fasse dependre le grade parfait d'une comparaison stricte.
        JudgementWindow nulle = JudgementWindow.None;
        Assert.Equal(JudgementGrade.Perfect, nulle.Grade(0.0));
        Assert.Equal(JudgementGrade.None, nulle.Grade(0.0001));
    }

    [Fact]
    public void Les_Fenetres_Sont_Ordonnees_Pour_Chaque_Niveau()
    {
        foreach (ChartLevel level in ChartLevels.All)
        {
            JudgementWindow window = JudgementWindows.ForLevel(level);
            Assert.True(window.Perfect <= window.Great, $"{level} : Perfect doit tenir dans Great.");
            Assert.True(window.Great <= window.Good, $"{level} : Great doit tenir dans Good.");
            Assert.True(window.Good > 0, $"{level} : une fenetre nulle ne pardonne aucune latence.");
        }
    }

    [Fact]
    public void Cascade_Est_Plus_Stricte_Que_Ronde_Est_Plus_Souple_Que_Berceau()
    {
        // Le sens de l'echelle est une decision de design, pas un hasard : plus
        // le niveau est difficile, plus la cible est petite. Un refactor qui
        // inverse l'ordre rend le mode difficile plus facile que le mode berceau.
        JudgementWindow berceau = JudgementWindows.ForLevel(ChartLevel.Berceau);
        JudgementWindow ronde = JudgementWindows.ForLevel(ChartLevel.Ronde);
        JudgementWindow cascade = JudgementWindows.ForLevel(ChartLevel.Cascade);

        Assert.True(cascade.Good < ronde.Good, "cascade doit etre plus strict que ronde.");
        Assert.True(ronde.Good < berceau.Good, "ronde doit etre plus strict que berceau.");
    }

    [Fact]
    public void Les_Fenetres_Restent_Sous_Le_Temps_De_Reaction()
    {
        // wiki: generateur.md 6.2 fixe 220 ms et 350 ms comme temps de reaction
        // du joueur en berceau. Une fenetre plus large que le temps de reaction
        // ne mesurerait plus la precision mais l'inattention.
        JudgementWindow berceau = JudgementWindows.ForLevel(ChartLevel.Berceau);
        Assert.True(berceau.Good < 0.220, "la fenetre 'bien' depasse le temps de reaction.");
    }

    [Fact]
    public void Le_Score_Decroit_Avec_Le_Grade()
    {
        Assert.Equal(1000, JudgementWindows.ScoreFor(JudgementGrade.Perfect));
        Assert.Equal(600, JudgementWindows.ScoreFor(JudgementGrade.Great));
        Assert.Equal(250, JudgementWindows.ScoreFor(JudgementGrade.Good));
        Assert.Equal(0, JudgementWindows.ScoreFor(JudgementGrade.None));
    }

    [Fact]
    public void Le_Score_Est_Normalise_A_Un_Million()
    {
        // wiki: generateur.md 7.1 : la note finale est ramenee sur 1 000 000,
        // donc le score d'une note doit s'exprimer en milliemes de million.
        foreach (JudgementGrade grade in Enum.GetValues<JudgementGrade>())
        {
            int score = JudgementWindows.ScoreFor(grade);
            Assert.InRange(score, 0, 1000);
        }
    }

    [Fact]
    public void Un_Niveau_Inconnu_Est_Refuse()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => JudgementWindows.ForLevel((ChartLevel)42));
    }
}
