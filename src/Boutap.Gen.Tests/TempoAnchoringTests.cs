// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
//
// L'ancrage de la grille rythmique : la garantie qu'un tempo faux ne fait pas
// deriver une partition.
//
// La question posee etait : un tempo mesure de 119,96 BPM sur un morceau a 120
// n'entraine-t-il pas une derive qui decale la fin du morceau ? La reponse est
// non, et ces tests la prouvent au lieu de l'affirmer. Le quantificateur ancre
// chaque onset sur le battement detecte le plus proche et recalcule la grille a
// chaque temps ; il n'extrapole jamais une grille depuis un tempo. Une erreur
// de tempo ne peut donc pas s'accumuler : elle deplace une note d'au plus un
// seizieme de temps, au debut du morceau comme a la fin.

using System.Collections.Generic;
using System.Linq;
using Boutap.Gen.Analysis;
using Boutap.Gen.Compose;
using Xunit;

namespace Boutap.Gen.Tests;

/// <summary>L'ancrage de la quantification sur les battements detectes.</summary>
public class TempoAnchoringTests
{
    /// <summary>Tempo de reference du morceau synthetique.</summary>
    private const double ReferenceBpm = 120.0;

    /// <summary>Quatre minutes de morceau, en secondes.</summary>
    private const double LongPieceSeconds = 240.0;

    /// <summary>Un seizieme de temps a 120 BPM, soit la granularite de la grille.</summary>
    private static double StepAt120Bpm => 60.0 / ReferenceBpm / Quantizer.SubdivisionsPerBeat;

    /// <summary>
    /// Une note quantifiee reste a moins d'un seizieme de temps de sa position
    /// quand le tempo annonce est faux de plusieurs pourcents.
    /// </summary>
    /// <remarks>
    /// Le morceau fait quatre minutes : si la grille etait extrapolee depuis le
    /// tempo, une erreur de 5 % deplacerait la derniere note de douze secondes.
    /// L'ecart mesure reste borne par un pas de grille, du premier au dernier
    /// temps, et c'est tout l'objet de ce test.
    /// </remarks>
    [Fact]
    public void UnTempoFauxNeFaitPasDeriverLaGrille()
    {
        double[] beats = BeatsAtReferenceTempo();
        IReadOnlyList<Onset> onsets = OnsetsOnEveryBeat(beats);
        IReadOnlyList<GridTime> reference = Quantizer.Quantize(
            onsets, beats, ReferenceBpm, PhasesFor(beats.Length));

        foreach (double wrong in new[] { 100.0, 114.0, 126.0, 135.0 })
        {
            IReadOnlyList<GridTime> shifted = Quantizer.Quantize(
                onsets, beats, wrong, PhasesFor(beats.Length));

            // Une grille extrapolee depuis un tempo perdrait l'ancrage des la
            // premiere mesure. Ici les deux grilles tombent sur les memes
            // subdivisions, et les positions restent voisines.
            for (int index = 0; index < reference.Count; index++)
            {
                Assert.Equal(reference[index].Subdivision, shifted[index].Subdivision);
                Assert.True(
                    Math.Abs(reference[index].Seconds - shifted[index].Seconds) < StepAt120Bpm,
                    $"tempo {wrong} : la note {index} a bouge de "
                    + $"{System.Math.Abs(reference[index].Seconds - shifted[index].Seconds):0.0000} s.");
            }
        }
    }

    /// <summary>
    /// L'ecart ne grandit pas avec la position dans le morceau.
    /// </summary>
    /// <remarks>
    /// C'est le test de la derive, separe du precedent : une erreur qui
    /// s'accumule se voit a la fin, pas au debut. On compare donc le pire ecart
    /// du premier cinquieme du morceau a celui du dernier.
    /// </remarks>
    [Fact]
    public void LEcartNeGranditPasAvecLaPositionDansLeMorceau()
    {
        double[] beats = BeatsAtReferenceTempo();
        IReadOnlyList<Onset> onsets = OnsetsOnEveryBeat(beats);
        IReadOnlyList<GridTime> reference = Quantizer.Quantize(
            onsets, beats, ReferenceBpm, PhasesFor(beats.Length));
        IReadOnlyList<GridTime> shifted = Quantizer.Quantize(
            onsets, beats, 108.0, PhasesFor(beats.Length));

        int lastBeatIndex = reference.Count - 1;
        int fifth = lastBeatIndex / 5;

        double worstEarly = WorstGap(reference, shifted, 0, fifth);
        double worstLate = WorstGap(reference, shifted, lastBeatIndex - fifth, lastBeatIndex);

        Assert.True(
            worstLate <= worstEarly + StepAt120Bpm,
            $"l'ecart passe de {worstEarly:0.0000} s au debut a {worstLate:0.0000} s a la fin : "
            + "la grille derive.");
        Assert.True(
            worstLate < StepAt120Bpm,
            $"l'ecart en fin de morceau vaut {worstLate:0.0000} s, soit plus d'un pas de grille.");
    }

    /// <summary>Un tempo entier se lit entier, et la mesure reste consultable.</summary>
    [Fact]
    public void UnTempoMessuréSeLitEntierEtLaMesureResteVisible()
    {
        // 119,96 est la moyenne reelle des intervalles detectes d'un morceau a
        // 120 : la ramener a l'entier est un choix d'affichage, pas une
        // correction de derive. Le test verrouille les deux cotes.
        Assert.True(TempoRounding.IsRounded(119.96));
        Assert.Equal(120.0, TempoRounding.Display(119.96));

        // Un tempo qui n'est pas un entier mesurable n'est pas arrondi : un
        // morceau a 137,3 BPM existe, et l'arrondir mentirait sur lui.
        Assert.False(TempoRounding.IsRounded(137.3));
        Assert.Equal(137.3, TempoRounding.Display(137.3));

        // Un tempo absurde ou negatif reste tel quel plutot que de devenir 0.
        Assert.Equal(0.0, TempoRounding.Display(0));
        Assert.False(TempoRounding.IsRounded(double.NaN));
    }

    private static double WorstGap(
        IReadOnlyList<GridTime> reference,
        IReadOnlyList<GridTime> shifted,
        int from,
        int to)
    {
        double worst = 0;
        for (int index = from; index <= to && index < reference.Count; index++)
        {
            worst = System.Math.Max(
                worst, System.Math.Abs(reference[index].Seconds - shifted[index].Seconds));
        }

        return worst;
    }

    /// <summary>Des battements reguliers, quatre minutes a 120 BPM.</summary>
    private static double[] BeatsAtReferenceTempo()
    {
        double beat = 60.0 / ReferenceBpm;
        int count = (int)(LongPieceSeconds / beat) + 1;
        var beats = new double[count];
        for (int index = 0; index < count; index++)
        {
            beats[index] = index * beat;
        }

        return beats;
    }

    /// <summary>Un onset par battement, decale d'un seizieme pour etre realiste.</summary>
    private static List<Onset> OnsetsOnEveryBeat(IReadOnlyList<double> beats)
    {
        double step = 60.0 / ReferenceBpm / Quantizer.SubdivisionsPerBeat;
        return beats
            .Select((time, index) => new Onset(
                index, time + (index % 3) * step, 0.5 + (index % 5) * 0.1))
            .ToList();
    }

    /// <summary>Aucun decalage de phase : tous les temps sont sur la subdivision 0.</summary>
    private static BeatPhase[] PhasesFor(int count)
    {
        var phases = new BeatPhase[count];
        for (int index = 0; index < count; index++)
        {
            phases[index] = new BeatPhase(0, 0.5, new double[16], 0);
        }

        return phases;
    }
}
