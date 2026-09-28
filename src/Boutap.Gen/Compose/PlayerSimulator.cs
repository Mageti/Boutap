// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Simulation de joueur : wiki generateur.md §6.2 et §8.
//
// Une partition peut etre valide au sens des dix regles de lisibilite et
// rester injouable : dix touches a 20 notes par seconde passent L1 a L8, et
// personne ne les jouera. La simulation corrige ce que les regles ne voient
// pas, parce qu'elle raisonne en temps de main et non en temps de symbole.

using System.Collections.ObjectModel;

using Boutap.Core.Pack;

namespace Boutap.Gen.Compose;

/// <summary>Temps de reaction et couts de transition d'un joueur simule.</summary>
/// <param name="ReactionSeconds">Le temps de reaction simple.</param>
/// <param name="SameHandStepSeconds">Cout d'un deplacement a la main, par touche franchie.</param>
/// <param name="OtherHandBaseSeconds">Cout de depart d'un changement de main.</param>
/// <param name="OtherHandStepSeconds">Cout supplementaire par touche franchie.</param>
public sealed record PlayerSimulatorOptions(
    double ReactionSeconds = 0.220,
    double SameHandStepSeconds = 0.055,
    double OtherHandBaseSeconds = 0.090,
    double OtherHandStepSeconds = 0.090)
{
    /// <summary>Les temps d'un adulte jeune et reposé, comme la specification les donne.</summary>
    public static PlayerSimulatorOptions Adult { get; } = new PlayerSimulatorOptions();

    /// <summary>Les temps d'un enfant de sept ans, pour le niveau BERCEAU.</summary>
    /// <remarks>
    /// L specification dit 220 ms en general et 350 ms en BERCEAU, en notant
    /// elle-meme que 220 ms est optimiste pour l'usage principal du projet.
    /// </remarks>
    public static PlayerSimulatorOptions Child { get; } =
        new PlayerSimulatorOptions() with { ReactionSeconds = 0.350 };

    /// <summary>Les temps correspondant a un niveau de difficulte.</summary>
    /// <param name="level">Le niveau de la partition.</param>
    /// <returns>Les temps a appliquer.</returns>
    public static PlayerSimulatorOptions ForLevel(ChartLevel level) =>
        level == ChartLevel.Berceau ? Child : Adult;
}

/// <summary>Une note que le joueur simule n'a pas le temps de jouer.</summary>
/// <param name="NoteIndex">L'index de la note dans la liste d'entree.</param>
/// <param name="TimeSeconds">Son instant, en secondes absolues.</param>
/// <param name="RequiredSeconds">Le temps qu'il lui fallait.</param>
/// <param name="AvailableSeconds">Le temps dont elle disposait.</param>
public readonly record struct UnreachableNote(
    int NoteIndex,
    double TimeSeconds,
    double RequiredSeconds,
    double AvailableSeconds);

/// <summary>Le resultat d'une simulation de joueur.</summary>
/// <param name="Reachable">Les notes que le joueur simule tient.</param>
/// <param name="Unreachable">Celles qu'il n'atteint pas.</param>
public sealed record SimulationReport(
    IReadOnlyList<Note> Reachable,
    IReadOnlyList<UnreachableNote> Unreachable);

/// <summary>Simulation d'un joueur sur une partition.</summary>
public static class PlayerSimulator
{
    /// <summary>Joue la partition et retire ce qu'aucun joueur moyen n'atteindrait.</summary>
    /// <param name="notes">Les notes, triees par instant croissant.</param>
    /// <param name="level">Le niveau, qui fixe le temps de reaction.</param>
    /// <param name="options">Les temps, ou <see langword="null"/> pour ceux du niveau.</param>
    /// <returns>Ce que le joueur tient et ce qu'il n'atteint pas.</returns>
    /// <remarks>
    /// On supprime, on ne deplace pas. Deplacer une note cree des motifs qui
    /// ne ressemblent plus a rien : le joueur entendrait le morceau et verrait
    /// un motif etranger. Une note en trop est un defaut visible, un motif
    /// deplace est un mensonge.
    /// </remarks>
    public static SimulationReport Simulate(
        IReadOnlyList<Note> notes,
        ChartLevel level,
        PlayerSimulatorOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(notes);
        PlayerSimulatorOptions settings = options ?? PlayerSimulatorOptions.ForLevel(level);

        List<Note> reachable = new(notes.Count);
        List<UnreachableNote> unreachable = [];
        Note? previous = null;

        for (int index = 0; index < notes.Count; index++)
        {
            Note note = notes[index];
            if (previous is null)
            {
                reachable.Add(note);
                previous = note;
                continue;
            }

            double available = note.Time - previous.Time;
            bool newPhrase = available > settings.ReactionSeconds;
            double required = Transition(previous, note, settings) + (newPhrase ? settings.ReactionSeconds : 0);
            if (available + 1e-9 < required)
            {
                unreachable.Add(new UnreachableNote(index, note.Time, required, available));
                continue;
            }

            reachable.Add(note);
            previous = note;
        }

        return new SimulationReport(new ReadOnlyCollection<Note>(reachable), new ReadOnlyCollection<UnreachableNote>(unreachable));
    }

    /// <summary>Le cout de passage d'une note a l'autre.</summary>
    /// <param name="from">La note precedente.</param>
    /// <param name="to">La note suivante.</param>
    /// <param name="options">Les temps du joueur simule.</param>
    /// <returns>Le cout, en secondes.</returns>
    public static double Transition(Note from, Note to, PlayerSimulatorOptions options)
    {
        if (to.IsHold && SameKeyPressed(from, to))
        {
            // La touche est deja enfoncee : la main n'a rien a faire.
            return 0;
        }

        int left = GridIndex(from.Key);
        int right = GridIndex(to.Key);
        if (left < 0 || right < 0)
        {
            // Un volant n'est pas sur la grille : le joueur change de main.
            return options.OtherHandBaseSeconds;
        }

        int distance = ChromaFold.Distance(left, right);
        if (distance == 0)
        {
            return options.SameHandStepSeconds;
        }

        return Hand(left) == Hand(right)
            ? options.SameHandStepSeconds * distance
            : options.OtherHandBaseSeconds + (options.OtherHandStepSeconds * distance);
    }

    /// <summary>La main qui joue une touche de la grille.</summary>
    /// <param name="gridIndex">L'index de touche, de 0 a 8.</param>
    /// <returns>0 pour la main gauche, 1 pour la main droite.</returns>
    /// <remarks>
    /// Convention de travail, a confirmer sur un joueur reel en S7.8 : la
    /// colonne de gauche et celle du milieu sont jouees a gauche, la colonne de
    /// droite a la main droite. Ce n'est pas dans la specification, qui ne dit
    /// rien de la repartition des mains, alors qu'elle raisonne dessus.
    /// </remarks>
    public static int Hand(int gridIndex) => (gridIndex / ChromaFold.Columns) >= 2 ? 1 : 0;

    private static bool SameKeyPressed(Note from, Note to)
    {
        if (!from.Key.HasValue || !to.Key.HasValue || from.Key != to.Key)
        {
            return false;
        }

        double end = from.EndTime;
        return from.IsHold && end > from.Time && to.Time >= from.Time && to.Time <= end;
    }

    private static int GridIndex(KeyBinding? key)
    {
        if (key is null || !key.Value.HasValue || !key.Value.IsGrid)
        {
            return -1;
        }

        for (int index = 0; index < ChromaFold.GridSize; index++)
        {
            if (key.Value == KeyBinding.Grid(index))
            {
                return index;
            }
        }

        return -1;
    }
}
