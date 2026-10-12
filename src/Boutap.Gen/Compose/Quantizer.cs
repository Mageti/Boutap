// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Boutap.Gen.Analysis;

namespace Boutap.Gen.Compose;

/// <summary>Un temps propose par le moteur, avant humanisation.</summary>
/// <param name="Seconds">Position sur la grille, en secondes absolues.</param>
/// <param name="Strength">Force de l'onset, entre 0 et 1.</param>
/// <param name="Subdivision">Position dans le temps, en doubles-croches.</param>
public readonly record struct GridTime(double Seconds, double Strength, int Subdivision);

/// <summary>Quantification des onsets sur la grille rythmique (wiki: generateur.md §4.9).</summary>
/// <remarks>
/// La phase du temps est mesuree, pas supposee constante : un morceau peut tres
/// bien avoir son temps sur la derniere double-croche d'une periode. Sans cette
/// mesure, la premiere note part sur la mauvaise subdivision et tout le reste
/// suit le decalage.
/// </remarks>
public static class Quantizer
{
    /// <summary>Nombre de doubles-croches par temps, impose par le format.</summary>
    public const int SubdivisionsPerBeat = 16;

    /// <summary>Propose des positions de grille pour une liste d'onsets.</summary>
    /// <param name="onsets">Onsets detectes, dans l'ordre.</param>
    /// <param name="beats">Battements de reference, croissants.</param>
    /// <param name="tempoBpm">Tempo mesure.</param>
    /// <param name="beatPhases">Phase mesuree de chaque temps, ou une liste vide.</param>
    /// <returns>Une proposition par onset conserve.</returns>
    public static IReadOnlyList<GridTime> Quantize(
        IReadOnlyList<Onset> onsets,
        IReadOnlyList<double> beats,
        double tempoBpm,
        IReadOnlyList<BeatPhase> beatPhases)
    {
        ArgumentNullException.ThrowIfNull(onsets);
        ArgumentNullException.ThrowIfNull(beats);
        ArgumentNullException.ThrowIfNull(beatPhases);

        if (onsets.Count == 0 || beats.Count == 0 || tempoBpm <= 0)
        {
            return [];
        }

        double beatSeconds = 60.0 / tempoBpm;
        double stepSeconds = beatSeconds / SubdivisionsPerBeat;
        var quantized = new List<GridTime>(onsets.Count);

        foreach (Onset onset in onsets)
        {
            int beat = BeatAt(onset.TimeSeconds, beats, beatSeconds);
            double beatStart = beats[beat];
            int phase = StrongestPhaseFor(beat, beatPhases);

            // Le battement detecte tombe sur la subdivision la plus energique du
            // temps : le temps « fort » commence donc phase doubles-croches avant
            // la position nominale, et c'est lui qui sert de reference.
            double downbeat = beatStart - phase * stepSeconds;
            double offset = onset.TimeSeconds - downbeat;
            int subdivision = (int)Math.Round(offset / stepSeconds, MidpointRounding.AwayFromZero);
            if (subdivision < 0)
            {
                subdivision = 0;
            }

            if (subdivision >= SubdivisionsPerBeat)
            {
                // L'onset appartient au temps suivant, pas a la fin de celui-ci.
                // La correction peut depasser une fois le temps comple : un onset
                // a plus d'un temps apres le dernier battement detecte (fin de
                // morceau, battements manques) arrive avec deux temps d'avance.
                // Le repli doit donc manger tous les temps de trop, pas un seul,
                // sinon la subdivision reste superieure a 16 et ne correspond a
                // aucune position de la grille.
                while (subdivision >= SubdivisionsPerBeat)
                {
                    downbeat += beatSeconds;
                    subdivision -= SubdivisionsPerBeat;
                }
            }

            double seconds = Math.Max(0, downbeat + subdivision * stepSeconds);
            quantized.Add(new GridTime(seconds, onset.Strength, subdivision));
        }

        return quantized;
    }

    /// <summary>Index du dernier battement au plus tard apres cet instant.</summary>
    /// <remarks>
    /// Un onset appartient au temps qu'il traverse, c'est-a-dire au dernier
    /// battement detecte qui le precede. La version precedente tolérait un
    /// demi-temps d'avance (`time >= beats[i] - beatSeconds / 2`), ce qui
    /// rattachait l'onset au battement SUIVANT. Le recalage de phase
    /// (`downbeat = beatStart - phase * stepSeconds`) ramenait alors l'offset
    /// a une ou zero subdivisions : tout le morceau atterrissait sur la
    /// subdivision 0.
    /// <para>
    /// La consequence n'etait pas cosmetique. Les trois niveaux se differencient
    /// par le nombre de subdivisions qu'ils jouent, via
    /// `subdivision % step == 0`. Si tout est sur la subdivision 0, les trois
    /// profils gardent exactement les memes onsets : berceau, ronde et cascade
    /// devenaient le meme morceau avec un habillage different, et la difficulte
    /// ne differencait plus rien.
    /// </para>
    /// </remarks>
    private static int BeatAt(double time, IReadOnlyList<double> beats, double beatSeconds)
    {
        for (int i = beats.Count - 1; i >= 0; i--)
        {
            if (time >= beats[i])
            {
                return i;
            }
        }

        return 0;
    }

    private static int StrongestPhaseFor(int beat, IReadOnlyList<BeatPhase> beatPhases)
    {
        if (beat < 0 || beat >= beatPhases.Count)
        {
            return 0;
        }

        return beatPhases[beat].StrongestPhase;
    }
}
