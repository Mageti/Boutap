// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Humanisation et nettoyage : wiki generateur.md §6.3 et §6.4.
//
// Une partition parfaitement reguliere sonne robotique, et c'est le defaut
// classique des generateurs. On l'introduit deterministiquement : la graine
// derive du contenu du fichier audio, jamais de l'heure ni d'un Guid, donc
// deux executions sur deux machines différentes produisent le meme fichier.

using System.Collections.ObjectModel;

using Boutap.Core.Determinism;
using Boutap.Core.Pack;

namespace Boutap.Gen.Compose;

/// <summary>Les reglages d'humanisation.</summary>
/// <param name="JitterBeats">Amplitude du jitter, en temps.</param>
/// <param name="HoldMinimumFactor">Facteur minimal de la duree d'un tenu.</param>
/// <param name="HoldMaximumFactor">Facteur maximal de la duree d'un tenu.</param>
/// <param name="SyncopeProbability">Probabilite d'avancer une note d'une sous-case.</param>
public sealed record HumanizerOptions(
    double JitterBeats = 0.35,
    double HoldMinimumFactor = 0.5,
    double HoldMaximumFactor = 1.5,
    double SyncopeProbability = 0.08)
{
    /// <summary>Les reglages de la specification, sans modification.</summary>
    public static HumanizerOptions Specification { get; } = new HumanizerOptions();
}

/// <summary>L'introduction d'une variation humaine, deterministe.</summary>
public static class Humanizer
{
    /// <summary>Le nombre de sous-cases d'un temps, donc la finesse du jitter.</summary>
    /// <remarks>
    /// Seize sous-cases : c'est la resolution de la grille de quantification du
    /// pipeline, donc la seule finesse ou une variation a encore du sens.
    /// </remarks>
    public const int Subdivisions = 16;

    /// <summary>Introduit le jitter, la variation de tenue et la syncope.</summary>
    /// <param name="notes">Les notes a humaniser, qui ne sont pas modifiees.</param>
    /// <param name="audioSha256Hex">L'empreinte du contenu audio decode.</param>
    /// <param name="generatorVersion">La version du generateur, inscription dans la graine.</param>
    /// <param name="level">Le niveau, inscription dans la graine.</param>
    /// <param name="tempoBpm">Le tempo, pour traduire un temps en secondes.</param>
    /// <param name="options">Les reglages, ou <see langword="null"/> pour ceux de la spec.</param>
    /// <returns>Les notes humanisees, dans le meme ordre.</returns>
    public static IReadOnlyList<Note> Humanize(
        IReadOnlyList<Note> notes,
        string audioSha256Hex,
        string generatorVersion,
        ChartLevel level,
        double tempoBpm,
        HumanizerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(notes);
        if (notes.Count == 0)
        {
            return Array.Empty<Note>();
        }

        if (!(tempoBpm > 0))
        {
            throw new ArgumentOutOfRangeException(nameof(tempoBpm), tempoBpm, "Le tempo doit etre positif pour humaniser.");
        }

        HumanizerOptions settings = options ?? HumanizerOptions.Specification;
        Xorshift128Plus random = SeedDerivation.CreateGenerator(audioSha256Hex, generatorVersion, level.FileNameOf());
        double beatSeconds = 60.0 / tempoBpm;
        double amplitude = settings.JitterBeats * beatSeconds / Subdivisions;
        double subCase = beatSeconds / Subdivisions;

        List<Note> result = new(notes.Count);
        for (int index = 0; index < notes.Count; index++)
        {
            Note note = notes[index];
            double jitter = (random.NextDouble() - 0.5) * amplitude;
            double time = note.Time + jitter;

            if (random.NextDouble() < settings.SyncopeProbability)
            {
                // Une syncope avance la note d'une sous-case. Elle ne recule
                // jamais : un retard eviterait la note suivante, et c'est
                // l'inverse de ce qu'une syncope fait en musique.
                time += subCase;
            }

            double? duration = note.Duration;
            if (duration is > 0)
            {
                double factor = settings.HoldMinimumFactor
                    + (random.NextDouble() * (settings.HoldMaximumFactor - settings.HoldMinimumFactor));
                duration = duration.Value * factor;
            }

            result.Add(note with { Time = time, Duration = duration });
        }

        return new ReadOnlyCollection<Note>(result);
    }
}

/// <summary>Le nettoyage d'une partition avant ecriture.</summary>
public static class NoteCleaner
{
    /// <summary>Ecart sous lequel deux notes identiques sont fusionnees, en secondes.</summary>
    /// <remarks>
    /// Dix millisecondes, soit largement moins que la fenetre de jugement la
    /// plus resserree du jeu. Deux notes a moins de dix millisecondes l'une de
    /// l'autre sont, pour le joueur, une seule note.
    /// </remarks>
    public const double MergeSeconds = 0.010;

    /// <summary>Nettoie une partition comme le demande la specification.</summary>
    /// <param name="notes">Les notes, triees par instant croissant.</param>
    /// <param name="durationSeconds">La duree du morceau.</param>
    /// <param name="firstOnsetSeconds">Le premier onset du morceau.</param>
    /// <returns>Les notes survivantes, dans l'ordre.</returns>
    public static IReadOnlyList<Note> Clean(
        IReadOnlyList<Note> notes,
        double durationSeconds,
        double firstOnsetSeconds)
    {
        ArgumentNullException.ThrowIfNull(notes);

        List<Note> kept = [];
        double end = durationSeconds - 1.0;
        foreach (Note original in notes)
        {
            Note note = original;
            if (note.Time > end)
            {
                continue;
            }

            if (note.Time < firstOnsetSeconds)
            {
                continue;
            }

            if (note.Duration is <= 0)
            {
                note = note with { Duration = null };
            }

            if (kept.Count > 0)
            {
                Note previous = kept[^1];
                if (previous.Key == note.Key && Math.Abs(previous.Time - note.Time) < MergeSeconds)
                {
                    kept[^1] = previous with
                    {
                        Duration = Math.Max(previous.Duration ?? 0, note.Duration ?? 0) > 0
                            ? Math.Max(previous.Time + (previous.Duration ?? 0), note.Time + (note.Duration ?? 0)) - previous.Time
                            : null,
                    };
                    continue;
                }
            }

            kept.Add(note);
        }

        return new ReadOnlyCollection<Note>(kept);
    }
}
