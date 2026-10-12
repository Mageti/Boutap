// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Boutap.Gen.Analysis;
using Boutap.Gen.Compose;
using Xunit;

namespace Boutap.Gen.Tests;

/// <summary>
/// Verrouille l'ancrage d'un onset sur le temps qu'il traverse.
/// </summary>
/// <remarks>
/// <para>
/// Ces tests protegent une correction dont la consequence n'est pas cosmetique.
/// L'ancien <c>BeatAt</c> rattachait un onset situe juste avant un battement
/// detecte au battement SUIVANT, avec une tolerance d'un demi-temps. Le
/// recalage de phase ramenait alors l'offset a zero ou une subdivision, et tout
/// le morceau atterrissait sur la subdivision 0.
/// </para>
/// <para>
/// Les trois niveaux se distinguent par <c>subdivision % step == 0</c> avec
/// <c>step = 16 / KeptSubdivisions</c>. Si tout tombe sur 0, berceau, ronde et
/// cascade gardent exactement les memes notes : le « meme morceau lu
/// autrement » devient le meme morceau trois fois. La tolerance d'un demi-temps
/// ne falsifiait pas la mesure du tempo, elle tuait la differenciation.
/// </para>
/// </remarks>
public sealed class BeatAtAnchoringTests
{
    private const double ReferenceBpm = 120.0;

    private static double BeatSeconds => 60.0 / ReferenceBpm;

    private static double StepSeconds => BeatSeconds / Quantizer.SubdivisionsPerBeat;

    /// <summary>Battements reguliers, un toutes les 0,5 s.</summary>
    private static double[] Beats(int count) =>
        Enumerable.Range(0, count).Select(i => i * BeatSeconds).ToArray();

    /// <summary>Aucune phase mesuree : le recalage ne doit rien compenser.</summary>
    private static BeatPhase[] Phases(int count) =>
        Enumerable.Range(0, count).Select(_ => new BeatPhase(0, 0.5, new double[16], 0)).ToArray();

    private static List<Onset> OnsetsAt(params double[] times) =>
        times.Select((t, i) => new Onset(i, t, 0.5)).ToList();

    private static GridTime OnlyGrid(IReadOnlyList<GridTime> grid)
    {
        Assert.Single(grid);
        return grid[0];
    }

    /// <summary>
    /// Un onset pose juste avant un battement appartient au temps qu'il traverse.
    /// </summary>
    /// <remarks>
    /// A <c>beats[4] - 2 seiziemes</c>, l'onset est encore dans le quatrieme
    /// temps. Il doit donc sortir en subdivision 14, pas revenir en 0 sur le
    /// temps suivant. C'est le test qui verrouille <c>BeatAt</c>.
    /// </remarks>
    [Fact]
    public void UnOnsetJusteAvantLeBattementAppartientAuTempsQuIlTraverse()
    {
        double[] beats = Beats(10);
        double target = beats[4] - (2 * StepSeconds);

        IReadOnlyList<GridTime> grid = Quantizer.Quantize(
            OnsetsAt(target), beats, ReferenceBpm, Phases(beats.Length));

        GridTime only = OnlyGrid(grid);

        // Le temps traverse est le quatrieme : son offset vaut 14 seiziemes.
        Assert.Equal(14, only.Subdivision);
        Assert.InRange(only.Seconds, target - StepSeconds, target + StepSeconds);
    }

    /// <summary>
    /// Un onset a mi-chemin entre deux battements reste sur son propre temps.
    /// </summary>
    /// <remarks>
    /// C'est le cas ou l'ancienne tolerance d'un demi-temps se voyait le plus :
    /// a exactement <c>beats[3] - beatSeconds/2</c>, l'onset etait attribue au
    /// battement suivant, donc ramene en subdivision 0 du temps d'apres.
    /// </remarks>
    [Fact]
    public void UnOnsetAMiCheminEntreDeuxBattementsResteSurSonTemps()
    {
        double[] beats = Beats(10);
        double target = beats[3] - (BeatSeconds / 2.0);

        IReadOnlyList<GridTime> grid = Quantizer.Quantize(
            OnsetsAt(target), beats, ReferenceBpm, Phases(beats.Length));

        GridTime only = OnlyGrid(grid);

        // Huit seiziemes apres le battement precedent.
        Assert.Equal(8, only.Subdivision);
    }

    /// <summary>
    /// Un onset loin apres le dernier battement detecte sort proprement de la grille.
    /// </summary>
    /// <remarks>
    /// Fin de morceau ou battements manques : l'onset peut arriver deux temps
    /// apres le dernier battement connu. L'ancien repli ne corrigeait qu'un
    /// seul depassement, donc une subdivision superieure a 31 sortait de la
    /// grille et denoncait une note non triee en aval.
    /// </remarks>
    [Fact]
    public void UnOnsetApresLeDernierBattementSortDeLaGrilleProprement()
    {
        double[] beats = Beats(6);
        double target = beats[5] + (2.5 * BeatSeconds);

        IReadOnlyList<GridTime> grid = Quantizer.Quantize(
            OnsetsAt(target), beats, ReferenceBpm, Phases(beats.Length));

        GridTime only = OnlyGrid(grid);

        // Deux temps et demi apres le dernier battement : cinq seiziemes de
        // plus que le debut du cinquieme temps suivant.
        Assert.InRange(only.Subdivision, 0, Quantizer.SubdivisionsPerBeat - 1);
        Assert.True(only.Seconds > beats[5], "L'onset doit rester apres le dernier battement.");
    }

    /// <summary>
    /// Un onset avant le premier battement ne produit pas de temps negatif.
    /// </summary>
    /// <remarks>
    /// Le repli <c>Math.Max(0, ...)</c> existe pour ca, mais il ecrase deux
    /// notes en t=0 sans le dire si la grille part dans le negatif. On verifie
    /// ici que la subdivision retombe dans la grille plutot que d'etre aplatie.
    /// </remarks>
    [Fact]
    public void UnOnsetAvantLePremierBattementNeDevientPasNegatif()
    {
        double[] beats = Beats(8);
        double target = beats[0] - (3 * StepSeconds);

        IReadOnlyList<GridTime> grid = Quantizer.Quantize(
            OnsetsAt(target), beats, ReferenceBpm, Phases(beats.Length));

        GridTime only = OnlyGrid(grid);

        Assert.True(only.Seconds >= 0, "Aucun temps negatif ne doit sortir du quantificateur.");
        Assert.InRange(only.Subdivision, 0, Quantizer.SubdivisionsPerBeat - 1);
    }
}
