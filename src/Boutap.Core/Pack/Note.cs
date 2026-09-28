// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json.Serialization;

namespace Boutap.Core.Pack;

/// <summary>
/// Une note. Le modele ne contient que les six champs du schema : ni main, ni
/// type de note, et rien de plus.
/// </summary>
/// <remarks>
/// <para>
/// Les trois champs optionnels sont des <c>nullable</c> sur le modele, parce
/// que le schema leur donne une valeur par defaut. Un champ nullable se
/// distingue d'un champ absent dans le JSON ; une valeur concrete ne le peut
/// pas, puisque 8 et 1 sont des valeurs comme les autres. Les accesseurs
/// « …OrDefault » donnent la valeur effective et n'ecrivent jamais dans le
/// JSON.
/// </para>
/// <para>
/// Ce que le schema ne dit pas, et qu'il faut dire ici :
/// <list type="bullet">
/// <item><description>
/// <c>tap</c> et <c>hold</c> se deduisent de <c>d</c> ; <c>wheel</c> se deduit
/// de <c>k</c> ; un accord est deux notes de meme <c>t</c>.
/// </description></item>
/// <item><description>
/// <c>jack</c> et <c>slide</c> ne sont <em>pas</em> representables : ils
/// demandent de savoir de quelle main vient la note, or le schema n'a qu'une
/// touche. La V1 les declare absents plutot que de les encoder par
/// convention. Voir wiki: format.md.
/// </description></item>
/// </list>
/// </para>
/// </remarks>
public sealed record Note
{
    /// <summary>Temps d'arrivee, en secondes absolues dans la piste (regle N1).</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("t")]
    public double Time { get; init; }

    /// <summary>Touche : 0 a 8 pour la grille 3x3, <c>WHEEL_L</c> ou <c>WHEEL_R</c>.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("k")]
    public KeyBinding Key { get; init; }

    /// <summary>Duree de maintien. Absente ou nulle : note simple.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("d")]
    public double? Duration { get; init; }

    /// <summary>Force du coup, 0 a 8. N'agit que sur le rendu, jamais sur la fenetre.</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("v")]
    public int? Force { get; init; }

    /// <summary>Multiplicateur de largeur de fenetre de jugement, de 0,5 a 3.</summary>
    [JsonPropertyOrder(4)]
    [JsonPropertyName("w")]
    public double? WindowScale { get; init; }

    /// <summary>Decalage de phase, parmi 0, 1/8, 1/4 et 3/8 de temps musical.</summary>
    [JsonPropertyOrder(5)]
    [JsonPropertyName("s")]
    public double? PhaseShift { get; init; }

    /// <summary>Force effective, <see cref="PackFormat.DefaultForce"/> si absente.</summary>
    [JsonIgnore]
    public int ForceOrDefault => Force ?? PackFormat.DefaultForce;

    /// <summary>Fenetre effective, <see cref="PackFormat.DefaultWindowScale"/> si absente.</summary>
    [JsonIgnore]
    public double WindowScaleOrDefault => WindowScale ?? PackFormat.DefaultWindowScale;

    /// <summary>Phase effective, <see cref="PackFormat.DefaultPhaseShift"/> si absente.</summary>
    [JsonIgnore]
    public double PhaseShiftOrDefault => PhaseShift ?? PackFormat.DefaultPhaseShift;

    /// <summary>Vrai si la note est une tenue.</summary>
    [JsonIgnore]
    public bool IsHold => Duration is > 0;

    /// <summary>Vrai si la note est sur un volant.</summary>
    [JsonIgnore]
    public bool IsWheel => Key.IsWheel;

    /// <summary>Fin de la note, en secondes. Egale <see cref="Time"/> pour une note simple.</summary>
    [JsonIgnore]
    public double EndTime => Time + (Duration ?? 0);

    /// <summary>Construit une note simple, avec les valeurs par defaut du schema.</summary>
    public static Note Tap(double time, KeyBinding key) => new() { Time = time, Key = key };

    /// <summary>Construit une tenue.</summary>
    public static Note Hold(double time, KeyBinding key, double duration) =>
        new() { Time = time, Key = key, Duration = duration };

    /// <summary>Indique si une duree de maintien est plausible (regle de validite).</summary>
    public static bool IsValidHoldDuration(double duration) =>
        duration > 0 && duration <= PackFormat.MaxHoldSeconds;
}
