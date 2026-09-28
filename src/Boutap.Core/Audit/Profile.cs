// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json.Serialization;

namespace Boutap.Core.Audit;

/// <summary>Une touche declaree par un profil.</summary>
/// <remarks>
/// Le format de profil est le seul du depot qui n'a pas de schema JSON. Ses
/// regles sont donc en code, et l'audit s'appuie dessus.
/// </remarks>
public sealed record ProfileButton
{
    /// <summary>Types acceptes pour une touche de la grille ou un volant.</summary>
    public static IReadOnlyList<string> Kinds { get; } = new[] { "key", "gamepad_button", "axis", "none" };

    /// <summary>Types acceptes pour une entree de <c>buttons</c>.</summary>
    public static IReadOnlyList<string> ButtonKinds { get; } = new[] { "key", "gamepad_button", "none" };

    /// <summary>Type de l'entree : <c>key</c>, <c>gamepad_button</c>, <c>axis</c> ou <c>none</c>.</summary>
    [JsonPropertyName("type")]
    [JsonPropertyOrder(1)]
    public string? Kind { get; init; }

    /// <summary>Code de touche, de bouton ou d'axe, tel que le systeme le nomme.</summary>
    [JsonPropertyName("code")]
    [JsonPropertyOrder(2)]
    public string? Code { get; init; }
}

/// <summary>Un volant, c'est-a-dire une source analogique ou un repli discret.</summary>
/// <remarks>
/// <para>
/// Un volant declare deux choses distinctes, et c'est volontaire :
/// <c>buttons.WHEEL_L</c> est le <em>repli</em> discret (une touche de clavier,
/// pour un joueur qui n'a pas de manette) et <c>wheels.L</c> est la source
/// <em>analogique</em>, avec sa zone morte et son seuil de declenchement.
/// </para>
/// <para>
/// Les deux n'ont aucune raison de se correspondre : sur une vraie manette,
/// l'une est un bouton et l'autre un axe. Un audit qui exigerait leur egalite
/// obligerait a choisir, donc a perdre l'une des deux.
/// </para>
/// </remarks>
public sealed record ProfileWheel
{
    /// <summary>Type de l'entree : <c>axis</c>, <c>key</c>, <c>gamepad_button</c> ou <c>none</c>.</summary>
    [JsonPropertyName("type")]
    [JsonPropertyOrder(1)]
    public string? Kind { get; init; }

    /// <summary>Code de touche, de bouton ou d'axe.</summary>
    [JsonPropertyName("code")]
    [JsonPropertyOrder(2)]
    public string? Code { get; init; }

    /// <summary>Vrai si l'axe doit etre lu a l'envers.</summary>
    [JsonPropertyName("invert")]
    [JsonPropertyOrder(3)]
    public bool? Invert { get; init; }

    /// <summary>Zone morte, en [-1 ; 1] : en dessous, le volant est tenu pour immobile.</summary>
    [JsonPropertyName("deadzone")]
    [JsonPropertyOrder(4)]
    public double? Deadzone { get; init; }

    /// <summary>Lissage, dans [0 ; 1] : zero suit l'axe nu, un le suit beaucoup moins.</summary>
    [JsonPropertyName("smoothing")]
    [JsonPropertyOrder(5)]
    public double? Smoothing { get; init; }

    /// <summary>Seuil de declenchement, strictement au-dessus de la zone morte.</summary>
    [JsonPropertyName("trigger")]
    [JsonPropertyOrder(6)]
    public double? Trigger { get; init; }
}

/// <summary>Un profil de touches.</summary>
public sealed record PlayerProfile
{
    /// <summary>Version du format de profil, par exemple <c>1.0</c>.</summary>
    [JsonPropertyName("profile")]
    [JsonPropertyOrder(1)]
    public string? Profile { get; init; }

    /// <summary>Nom du profil, qui doit correspondre au nom du fichier.</summary>
    [JsonPropertyName("name")]
    [JsonPropertyOrder(2)]
    public string? Name { get; init; }

    /// <summary>Ce que le profil est cense etre.</summary>
    [JsonPropertyName("description")]
    [JsonPropertyOrder(3)]
    public string? Description { get; init; }

    /// <summary>Notes libres, typiquement la disposition du clavier.</summary>
    [JsonPropertyName("notes")]
    [JsonPropertyOrder(4)]
    public string? Notes { get; init; }

    /// <summary>Decalage de latence ajoute a l'horloge d'entree, en millisecondes.</summary>
    [JsonPropertyName("latency_ms")]
    [JsonPropertyOrder(5)]
    public int? LatencyMs { get; init; }

    /// <summary>
    /// Touches declarees, indexees par <c>T0</c> a <c>T8</c>, plus
    /// <c>WHEEL_L</c> et <c>WHEEL_R</c> si le profil a des replis discrets.
    /// </summary>
    [JsonPropertyName("buttons")]
    [JsonPropertyOrder(6)]
    public IReadOnlyDictionary<string, ProfileButton>? Buttons { get; init; }

    /// <summary>Sources de volant, indexees par <c>L</c> et <c>R</c>. Peut etre vide.</summary>
    [JsonPropertyName("wheels")]
    [JsonPropertyOrder(7)]
    public IReadOnlyDictionary<string, ProfileWheel>? Wheels { get; init; }

    /// <summary>Nombre de joueurs que le profil decrit.</summary>
    [JsonPropertyName("players")]
    [JsonPropertyOrder(8)]
    public int? Players { get; init; }
}
