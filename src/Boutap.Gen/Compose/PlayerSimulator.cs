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
    /// <summary>
    /// Fenetre dans laquelle deux notes comptent comme un seul geste.
    /// </summary>
    /// <remarks>
    /// Un accord, c'est plusieurs touches pressees au meme instant. Le simulateur
    /// les voyait comme des notes successives separees de zero seconde : la
    /// deuxieme et la troisieme d'un accord de trois touches etaient donc
    /// toujours declarees inatteignables, et le generateur ne produisait aucun
    /// accord — soixante et onze notes sur cent quatre, sur le morceau de
    /// demonstration. La fenetre est large devant l'imprecision d'arrondi et
    /// etroite devant le plus rapide des motifs : a deux cents temps par minute,
    /// une double-croche dure encore soixante-quinze millisecondes.
    /// </remarks>
    private const double GestureWindowSeconds = 0.010;

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
    /// <para>
    /// L'unite n'est pas la note, c'est le geste : les notes qui tombent dans la
    /// meme fenetre se pressent ensemble, et c'est la main entiere qui se deplace
    /// d'un geste a l'autre. Comparer note a note confondait « appuyer sur trois
    /// touches en meme temps » et « appuyer sur trois touches a la suite ».
    /// </para>
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

        // Les tenues encore enfoncees, et ce que chaque main pressait au dernier
        // geste qu'elle a tenu. Une main absente d'un geste repart du repos.
        List<(int Grid, double End)> activeHolds = [];
        List<int> previousLeft = [];
        List<int> previousRight = [];
        double previousTime = 0;
        bool hasPrevious = false;
        int index = 0;

        while (index < notes.Count)
        {
            double start = notes[index].Time;
            List<int> indices = [];
            while (index < notes.Count && notes[index].Time - start <= GestureWindowSeconds)
            {
                indices.Add(index);
                index++;
            }

            List<int> left = [];
            List<int> right = [];
            bool wheel = false;
            foreach (int noteIndex in indices)
            {
                Note note = notes[noteIndex];
                int grid = GridIndex(note.Key);
                if (grid < 0)
                {
                    // Une touche hors grille est un volant, ou rien du tout.
                    if (note.Key.HasValue)
                    {
                        wheel = true;
                    }

                    continue;
                }

                (Hand(grid) == 0 ? left : right).Add(grid);
            }

            // Une tenue terminee avant ce geste ne compte plus comme enfoncee.
            for (int held = activeHolds.Count - 1; held >= 0; held--)
            {
                if (activeHolds[held].End < start)
                {
                    activeHolds.RemoveAt(held);
                }
            }

            bool IsHeld(int grid)
            {
                foreach ((int heldGrid, _) in activeHolds)
                {
                    if (heldGrid == grid)
                    {
                        return true;
                    }
                }

                return false;
            }

            double required = Math.Max(
                Math.Max(
                    HandCost(left, previousLeft, IsHeld, settings),
                    HandCost(right, previousRight, IsHeld, settings)),
                wheel ? settings.OtherHandBaseSeconds : 0);

            if (hasPrevious)
            {
                double available = start - previousTime;
                bool newPhrase = available > settings.ReactionSeconds;
                required += newPhrase ? settings.ReactionSeconds : 0;

                if (available + 1e-9 < required)
                {
                    foreach (int noteIndex in indices)
                    {
                        unreachable.Add(
                            new UnreachableNote(noteIndex, notes[noteIndex].Time, required, available));
                    }

                    // Un geste rate ne devient pas la reference du suivant.
                    continue;
                }
            }

            foreach (int noteIndex in indices)
            {
                reachable.Add(notes[noteIndex]);
            }

            foreach (int noteIndex in indices)
            {
                Note note = notes[noteIndex];
                if (note.IsHold)
                {
                    int grid = GridIndex(note.Key);
                    if (grid >= 0 && note.EndTime > note.Time)
                    {
                        activeHolds.Add((grid, note.EndTime));
                    }
                }
            }

            previousLeft = left;
            previousRight = right;
            previousTime = start;
            hasPrevious = true;
        }

        return new SimulationReport(
            new ReadOnlyCollection<Note>(reachable),
            new ReadOnlyCollection<UnreachableNote>(unreachable));
    }

    /// <summary>
    /// Ce qu'une main doit faire pour tenir un geste : se placer sur les touches
    /// pressees ensemble, puis venir de la ou elle etait.
    /// </summary>
    /// <param name="keys">Les touches de ce geste qui reviennent a cette main.</param>
    /// <param name="previous">Ce que cette main pressait au geste precedemment tenu.</param>
    /// <param name="isHeld">Dit si une touche est deja enfoncee par une tenue.</param>
    /// <param name="options">Les temps du joueur simule.</param>
    /// <returns>Le cout, en secondes.</returns>
    private static double HandCost(
        List<int> keys,
        List<int> previous,
        Func<int, bool> isHeld,
        PlayerSimulatorOptions settings)
    {
        if (keys.Count == 0)
        {
            return 0;
        }

        // Se placer : deux touches voisines se pressent avec deux doigts, un
        // ecart de deux cases oblige la main a s'ouvrir.
        double placement = settings.SameHandStepSeconds * Span(keys);

        if (previous.Count == 0)
        {
            // La main etait au repos : elle doit se reveiller, ce qui coute plus
            // qu'un pas.
            return settings.OtherHandBaseSeconds + placement;
        }

        // On part de la touche precedente qui arrange le mieux, et on paye la
        // plus mauvaise touche du geste : la main se place une seule fois.
        int best = int.MaxValue;
        foreach (int from in previous)
        {
            int worst = 0;
            foreach (int to in keys)
            {
                worst = Math.Max(worst, isHeld(to) ? 0 : MoveSteps(from, to));
            }

            best = Math.Min(best, worst);
        }

        return (settings.SameHandStepSeconds * best) + placement;
    }

    /// <summary>Nombre de pas pour aller d'une touche a l'autre.</summary>
    /// <param name="from">Touche de depart.</param>
    /// <param name="to">Touche d'arrivee.</param>
    /// <returns>Le nombre de pas.</returns>
    /// <remarks>
    /// Une touche vers elle-meme n'est pas gratuite : il faut lever le doigt
    /// avant de le reposer. Une touche deja enfoncee par une tenue l'est, et
    /// c'est <see cref="HandCost"/> qui le sait, pas cette fonction.
    /// </remarks>
    private static int MoveSteps(int from, int to)
    {
        int distance = ChromaFold.Distance(from, to);
        return distance == 0 ? 1 : distance;
    }

    /// <summary>L'ecart maximal entre deux touches pressees ensemble par une main.</summary>
    /// <param name="keys">Les touches de la main, dans ce geste.</param>
    /// <returns>L'ecart, en pas.</returns>
    private static int Span(List<int> keys)
    {
        if (keys.Count <= 1)
        {
            return 0;
        }

        int span = 0;
        for (int a = 0; a < keys.Count; a++)
        {
            for (int b = a + 1; b < keys.Count; b++)
            {
                span = Math.Max(span, ChromaFold.Distance(keys[a], keys[b]));
            }
        }

        return span;
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
