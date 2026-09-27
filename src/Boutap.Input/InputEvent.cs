// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Boutap.Core.Pack;

namespace Boutap.Input;

/// <summary>Nature d'un evenement d'entree.</summary>
public enum InputEventKind
{
    /// <summary>Appui sur une touche.</summary>
    KeyDown,

    /// <summary>Relachement d'une touche.</summary>
    KeyUp,

    /// <summary>Rotation d'un volant.</summary>
    Wheel,
}

/// <summary>
/// Un evenement d'entree, horodate par le compteur monotone du systeme.
/// </summary>
/// <remarks>
/// <para>
/// L'horodatage est un <c>Stopwatch.GetTimestamp()</c> capture au moment de
/// l'evenement par la couche materiel, pas lu a posteriori. C'est la seule
/// facon d'obtenir une latence honnete : si l'on horodate en traitant
/// l'evenement, la latence de la file d'attente est deja dans la mesure.
/// </para>
/// <para>
/// Le type ne contient aucune donnee d'horloge murale, ce qui permet a
/// <c>check-no-wallclock.sh</c> de refuser ce motif partout ailleurs.
/// </para>
/// </remarks>
/// <param name="Timestamp">Compteur monotone, en unites de <c>Stopwatch.Frequency</c>.</param>
/// <param name="Kind">Nature de l'evenement.</param>
/// <param name="Key">Touche concernee.</param>
public readonly record struct InputEvent(long Timestamp, InputEventKind Kind, KeyBinding Key);
