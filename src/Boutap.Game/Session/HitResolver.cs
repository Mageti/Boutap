// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Boutap.Core.Pack;
using Boutap.Game.Judgement;

namespace Boutap.Game.Session;

/// <summary>Le verdict d'un coup porte sur une note, ou sur rien du tout.</summary>
/// <param name="Grade">
/// La qualite du coup, ou <see cref="JudgementGrade.None"/> si le coup n'a
/// touche aucune note.
/// </param>
/// <param name="Note">
/// La note visee, ou <see langword="null"/> si le coup est parti dans le vide.
/// </param>
/// <param name="Delta">
/// Ecart en secondes entre le geste et le moment vise. Positif si le geste est
/// en retard. Nul si aucune note n'est visee.
/// </param>
/// <param name="Index">Index dans la frise de la note visee, ou -1.</param>
/// <param name="Assisted">
/// Vrai si le coup a ete attire par une touche voisine de celle demandee. La
/// note visee reste la bonne note : seul le doigt s'est trompe.
/// </param>
public readonly record struct HitOutcome(
    JudgementGrade Grade,
    Note? Note,
    double Delta,
    int Index,
    bool Assisted)
{
    /// <summary>Un coup parti dans le vide.</summary>
    public static HitOutcome Empty => new(JudgementGrade.None, null, 0, -1, false);

    /// <summary>Vrai si le coup a touche quelque chose.</summary>
    public bool HasHit => Note is not null && Grade != JudgementGrade.None;
}

/// <summary>
/// L'arbitre : a quel coup correspond quelle note.
/// </summary>
/// <remarks>
/// <para>
/// La classe ne lit aucune horloge et ne decide d'aucun etat : on lui donne un
/// instant, elle rend un verdict. Toute la politique de jeu est donc lisible en
/// un endroit, et testable sans lancer une partie.
/// </para>
/// <para>
/// Trois regles, dans cet ordre :
/// </para>
/// <list type="number">
/// <item><description>
/// Une touche exacte gagne toujours. Si le joueur a presse la touche de la note
/// et que le coup est dans la fenetre, c'est cette note qui part, meme si une
/// note voisine serait tombee plus juste : le geste vise, il ne se trompe pas de
/// doigt.
/// </description></item>
/// <item><description>
/// A egalite de grade, la note la plus proche dans le temps gagne. Sur deux
/// notes de la meme touche a 40 ms d'ecart, le joueur visait la premiere.
/// </description></item>
/// <item><description>
/// Sans candidat exact, une touche voisine prend le coup. Cette aide au geste
/// est le coeur de C1 : un doigt qui glisse d'une case n'est pas une erreur de
/// lecture, c'est de la motricite. Elle ne peut jamais prendre une note que la
/// touche exacte aurait pu prendre, et elle est signalee dans le verdict pour
/// rester visible.
/// </description></item>
/// </list>
/// </remarks>
public static class HitResolver
{
    /// <summary>Arbitre un coup.</summary>
    /// <param name="key">La touche pressee.</param>
    /// <param name="hitTime">Instant du coup, sur l'horloge audio.</param>
    /// <param name="timeline">La frise.</param>
    /// <param name="ledger">L'etat d'avancement des notes.</param>
    /// <returns>Le verdict.</returns>
    public static HitOutcome Resolve(
        KeyBinding key,
        double hitTime,
        ChartTimeline timeline,
        HitLedger ledger)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        ArgumentNullException.ThrowIfNull(ledger);

        // Les notes avant cet index ont la fenetre fermee : les considerer
        // laisserait un coup tardif recolter une note deja manquee.
        int start = timeline.FirstJudgableIndex(hitTime);

        int exactIndex = -1;
        JudgementGrade exactGrade = JudgementGrade.None;
        double exactDistance = 0;

        int assistedIndex = -1;
        JudgementGrade assistedGrade = JudgementGrade.None;
        double assistedDistance = 0;

        for (int index = start; index < timeline.Count; index++)
        {
            Note note = timeline[index];

            // La fenetre est symetrique : au-dela, la note est encore a venir.
            // Les notes suivantes le sont aussi, la liste etant triee.
            JudgementWindow window = timeline.WindowOf(note);
            if (note.Time - window.Good > hitTime)
            {
                break;
            }

            if (ledger.IsResolved(index))
            {
                continue;
            }

            bool exact = note.Key == key;
            if (!exact && !KeyMap.AreAdjacent(key, note.Key))
            {
                continue;
            }

            JudgementGrade grade = timeline.Judge(note, hitTime);
            if (grade == JudgementGrade.None)
            {
                continue;
            }

            double distance = Math.Abs(hitTime - note.Time);
            if (exact)
            {
                if (exactIndex < 0 || grade > exactGrade || (grade == exactGrade && distance < exactDistance))
                {
                    exactIndex = index;
                    exactGrade = grade;
                    exactDistance = distance;
                }
            }
            else if (assistedIndex < 0 || grade > assistedGrade || (grade == assistedGrade && distance < assistedDistance))
            {
                assistedIndex = index;
                assistedGrade = grade;
                assistedDistance = distance;
            }
        }

        if (exactIndex >= 0)
        {
            return new HitOutcome(
                exactGrade,
                timeline[exactIndex],
                hitTime - timeline[exactIndex].Time,
                exactIndex,
                false);
        }

        if (assistedIndex >= 0)
        {
            return new HitOutcome(
                assistedGrade,
                timeline[assistedIndex],
                hitTime - timeline[assistedIndex].Time,
                assistedIndex,
                true);
        }

        return HitOutcome.Empty;
    }
}
