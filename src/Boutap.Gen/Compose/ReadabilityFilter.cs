// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Application des dix regles de lisibilite : wiki generateur.md §6.1.
//
// L'ordre compte. L1 a L8 s'appliquent note par note et retirent ce qui ne
// peut pas etre joue. L9 et L10 ne portent pas sur une note mais sur une
// absence de note, ou sur le contenu du fichier : elles ne retirent rien, elles
// constatent. Les faire mentir en retirant des notes pour satisfoire une
// regle qui ne parle pas de notes serait le pire des deux mondes.

using System.Collections.ObjectModel;

using Boutap.Core.Pack;

namespace Boutap.Gen.Compose;

/// <summary>Ce qu'une regle a retire, ou ce qu'elle a constate.</summary>
/// <param name="Rule">L'identifiant de la regle, de L1 a L10.</param>
/// <param name="NoteIndex">L'index de la note concernee dans la liste d'entree.</param>
/// <param name="Reason">Pourquoi elle a ete retiree ou signalee, en clair.</param>
public readonly record struct ReadabilityFinding(string Rule, int NoteIndex, string Reason);

/// <summary>Le resultat du lissage d'une partition.</summary>
/// <param name="Notes">Les notes survivantes, dans l'ordre.</param>
/// <param name="Removed">Les notes retirees.</param>
/// <param name="Findings">Les constats qui ne retirent rien.</param>
public sealed record ReadabilityReport(
    IReadOnlyList<Note> Notes,
    IReadOnlyList<ReadabilityFinding> Removed,
    IReadOnlyList<ReadabilityFinding> Findings)
{
    /// <summary>Un rapport vide, rendu quand il n'y a rien a corriger.</summary>
    public static ReadabilityReport Empty { get; } = new(
        Array.Empty<Note>(),
        Array.Empty<ReadabilityFinding>(),
        Array.Empty<ReadabilityFinding>());
}

/// <summary>Ce que le lissage a besoin de savoir du fichier audio.</summary>
public interface IAudioContext
{
    /// <summary>Le morceau est-il audible a cet instant ?</summary>
    /// <param name="timeSeconds">L'instant, en secondes absolues.</param>
    /// <returns><see langword="false"/> si l'instant tombe dans un silence du fichier.</returns>
    bool IsSilentAt(double timeSeconds);

    /// <summary>La duree d'un temps, en secondes.</summary>
    double TempoBpm { get; }
}

/// <summary>Le lissage d'une partition pour la rendre lisible.</summary>
public static class ReadabilityFilter
{
    /// <summary>Applique les regles L1 a L8, dans l'ordre de la specification.</summary>
    /// <param name="notes">Les notes, triees par instant croissant.</param>
    /// <returns>Les notes survivantes et le detail de ce qui a ete retire.</returns>
    public static ReadabilityReport Smooth(IReadOnlyList<Note> notes)
    {
        ArgumentNullException.ThrowIfNull(notes);

        List<Note> kept = [];
        List<ReadabilityFinding> removed = [];
        Note[] holds = notes.Where(note => note.IsHold).ToArray();

        for (int index = 0; index < notes.Count; index++)
        {
            Note note = notes[index];
            string? reason = Reject(note, kept, holds, index);
            if (reason is null)
            {
                kept.Add(note);
                continue;
            }

            removed.Add(new ReadabilityFinding(reason.Split(':', 2)[0], index, reason));
        }

        return new ReadabilityReport(
            new ReadOnlyCollection<Note>(kept),
            new ReadOnlyCollection<ReadabilityFinding>(removed),
            Array.Empty<ReadabilityFinding>());
    }

    /// <summary>Constate L9 et L10, sans retirer la moindre note.</summary>
    /// <param name="notes">Les notes survivantes du lissage.</param>
    /// <param name="context">Ce que le filtre sait du fichier audio.</param>
    /// <returns>Les constats, eventuellement vides.</returns>
    public static IReadOnlyList<ReadabilityFinding> Inspect(IReadOnlyList<Note> notes, IAudioContext? context)
    {
        ArgumentNullException.ThrowIfNull(notes);
        if (context is null || notes.Count == 0)
        {
            return Array.Empty<ReadabilityFinding>();
        }

        List<ReadabilityFinding> findings = [];
        double beatSeconds = context.TempoBpm > 0 ? 60.0 / context.TempoBpm : 0;

        for (int index = 0; index + 1 < notes.Count; index++)
        {
            double gap = notes[index + 1].Time - notes[index].Time;
            if (beatSeconds > 0 && gap > ReadabilityRules.MaxSilenceBeats * beatSeconds)
            {
                double density = Math.Max(2, gap);
                double before = (index + 1) / Math.Max(density, 1.0);
                double after = (notes.Count - index - 1) / Math.Max(density, 1.0);
                if (before >= ReadabilityRules.DenseNotesPerSecond && after >= ReadabilityRules.DenseNotesPerSecond)
                {
                    findings.Add(new ReadabilityFinding(
                        "L9",
                        index + 1,
                        $"L9 : silence de {gap:F3} s, plus de {ReadabilityRules.MaxSilenceBeats:F0} temps, entre deux zones denses. "
                        + "C'est une erreur de segmentation, pas un choix : decouper la partition plutot que deplacer une note."));
                }
            }
        }

        for (int index = 0; index < notes.Count; index++)
        {
            if (context.IsSilentAt(notes[index].Time))
            {
                findings.Add(new ReadabilityFinding(
                    "L10",
                    index,
                    "L10 : la note tombe dans un silence du fichier. Le joueur entendrait un creux et croirait a une erreur."));
            }
        }

        return new ReadOnlyCollection<ReadabilityFinding>(findings);
    }

    private static string? Reject(Note note, List<Note> kept, Note[] holds, int index)
    {
        int sameTime = 0;
        foreach (Note accepted in kept)
        {
            if (Math.Abs(accepted.Time - note.Time) > 1e-9)
            {
                break;
            }

            sameTime++;
            if (sameTime >= ReadabilityRules.MaxSimultaneous)
            {
                return "L3: trop de notes simultanees.";
            }
        }

        foreach (Note accepted in kept)
        {
            double gap = note.Time - accepted.Time;
            if (gap <= 0)
            {
                continue;
            }

            if (!accepted.Key.HasValue || !note.Key.HasValue)
            {
                continue;
            }

            if (accepted.Key == note.Key)
            {
                if (gap < ReadabilityRules.SameKeySeconds)
                {
                    return "L1: deux notes sur la meme touche trop rapprochees.";
                }

                continue;
            }

            if (gap < ReadabilityRules.DifferentKeySeconds)
            {
                return "L2: deux notes de touches differentes trop rapprochees.";
            }

            if (accepted.Key.IsGrid && note.Key.IsGrid
                && ChromaFold.AreAdjacent(GridIndex(accepted.Key), GridIndex(note.Key))
                && gap < ReadabilityRules.AdjacentKeySeconds)
            {
                return "L4: deux notes de touches voisines trop rapprochees.";
            }
        }

        if (kept.Count >= ReadabilityRules.MaxPerShortWindow
            && note.Time - kept[^ReadabilityRules.MaxPerShortWindow].Time < ReadabilityRules.ShortWindowSeconds)
        {
            return "L5: trop de notes dans une fenetre courte.";
        }

        if (kept.Count >= ReadabilityRules.MaxPerLongWindow
            && note.Time - kept[^ReadabilityRules.MaxPerLongWindow].Time < ReadabilityRules.LongWindowSeconds)
        {
            return "L6: trop de notes dans une seconde.";
        }

        foreach (Note hold in holds)
        {
            if (hold.Time > note.Time)
            {
                break;
            }

            // La note n'est pas comparee a elle-meme, et la premiere note
            // n'est comparee a rien : il n'y a rien devant elle. Sans cette
            // condition, une partition qui commence par un tenu perdait sa
            // premiere note, comparee a son propre debut de tenue.
            if (index > 0 && Math.Abs(hold.Time - note.Time) > 1e-9)
            {
                double start = hold.Time;
                double end = hold.EndTime;
                if (note.Time - start < ReadabilityRules.HoldClearanceSeconds && start - note.Time < ReadabilityRules.HoldClearanceSeconds)
                {
                    return "L7: note collee au debut d'un tenu.";
                }

                if (end > start && note.Time > start && note.Time < end
                    && (note.Time - start < ReadabilityRules.HoldClearanceSeconds || end - note.Time < ReadabilityRules.HoldClearanceSeconds))
                {
                    return "L7: note collee a un tenu.";
                }
            }
        }

        int burst = BurstLength(kept);
        if (burst >= ReadabilityRules.MaxPerBurst)
        {
            return "L8: rafale trop longue.";
        }

        return null;
    }

    private static int GridIndex(KeyBinding key)
    {
        for (int index = 0; index < ChromaFold.GridSize; index++)
        {
            if (key == KeyBinding.Grid(index))
            {
                return index;
            }
        }

        return 0;
    }

    /// <summary>Longueur de la rafale en cours : notes sur la meme touche, sans note entre elles.</summary>
    private static int BurstLength(List<Note> kept)
    {
        if (kept.Count == 0)
        {
            return 0;
        }

        Note last = kept[^1];
        if (!last.Key.HasValue)
        {
            return 0;
        }

        int length = 0;
        for (int index = kept.Count - 1; index >= 0; index--)
        {
            Note candidate = kept[index];
            if (candidate.Key != last.Key)
            {
                break;
            }

            if (last.Time - candidate.Time >= ReadabilityRules.SameKeySeconds)
            {
                break;
            }

            length++;
            last = candidate;
        }

        return length;
    }
}
