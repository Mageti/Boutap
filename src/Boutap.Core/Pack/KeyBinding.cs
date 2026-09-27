// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Boutap.Core.Pack;

/// <summary>
/// Touche d'un pave de neuf touches, ou volant.
/// </summary>
/// <remarks>
/// <para>
/// Le schema <c>chart.schema.json</c> note une touche soit par son index dans
/// la grille 3x3 lue ligne par ligne (0 a 8), soit par l'un des deux noms
/// <c>WHEEL_L</c> et <c>WHEEL_R</c>. Ce type est le seul endroit du projet ou
/// cette double representation existe ; tout le reste manipule des entiers ou
/// des enums.
/// </para>
/// <para>
/// La grille 3x3 est le pave de nine key, disposition de reference du
/// projet (wiki: spec.md §4.1) :
/// </para>
/// <code>
///     0 1 2
///     3 4 5       les index suivent les rangees, de gauche a droite
///     6 7 8       puis de haut en bas
/// </code>
/// <para>
/// Les six premieres touches sont la main gauche, les trois dernieres la main
/// droite. Rien dans le schema ne marque cette separation : elle se deduit de
/// l'index, ce qui est la raison pour laquelle un <c>jack</c> (toucher la
/// meme touche deux fois de main opposee) n'est pas representable en V1.
/// </para>
/// </remarks>
[JsonConverter(typeof(KeyBindingJsonConverter))]
public readonly struct KeyBinding : IEquatable<KeyBinding>
{
    /// <summary>Nombre de touches de la grille.</summary>
    public const int GridSize = 9;

    /// <summary>Nom de la touche de volant gauche.</summary>
    public const string WheelLeftName = "WHEEL_L";

    /// <summary>Nom de la touche de volant droite.</summary>
    public const string WheelRightName = "WHEEL_R";

    private readonly int _value;

    private KeyBinding(int value)
    {
        _value = value;
    }

    /// <summary>Vrai si la touche est une des neuf touches de la grille.</summary>
    public bool IsGrid => _value is >= 0 and < GridSize;

    /// <summary>Vrai si la touche est un des deux volants.</summary>
    public bool IsWheel => _value is WheelLeftCode or WheelRightCode;

    /// <summary>Vrai si la touche a ete initialisee.</summary>
    public bool HasValue => _value is >= 0 and < WheelRightCode + 1;

    private const int WheelLeftCode = 100;
    private const int WheelRightCode = 101;

    /// <summary>Une touche de la grille, lue ligne par ligne de 0 a 8.</summary>
    /// <param name="index">Index dans la grille.</param>
    /// <returns>La touche.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="index"/> n'est pas dans [0 ; 8].
    /// </exception>
    public static KeyBinding Grid(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, GridSize);
        return new KeyBinding(index);
    }

    /// <summary>Le volant gauche.</summary>
    public static KeyBinding WheelLeft => new(WheelLeftCode);

    /// <summary>Le volant droit.</summary>
    public static KeyBinding WheelRight => new(WheelRightCode);

    /// <summary>
    /// Lit une touche depuis sa forme JSON : un entier 0 a 8, ou l'un des deux
    /// noms de volant.
    /// </summary>
    /// <param name="text">Texte JSON de la touche.</param>
    /// <param name="key">Touche lue.</param>
    /// <returns>Vrai si <paramref name="text"/> est une touche valide.</returns>
    public static bool TryParse(string? text, out KeyBinding key)
    {
        key = default;
        if (text is null)
        {
            return false;
        }

        if (string.Equals(text, WheelLeftName, StringComparison.Ordinal))
        {
            key = WheelLeft;
            return true;
        }

        if (string.Equals(text, WheelRightName, StringComparison.Ordinal))
        {
            key = WheelRight;
            return true;
        }

        if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int index)
            && index is >= 0 and < GridSize)
        {
            key = new KeyBinding(index);
            return true;
        }

        return false;
    }

    /// <summary>Compare deux touches.</summary>
    /// <param name="other">Touche a comparer.</param>
    /// <returns>Vrai si les deux touches designent la meme cible.</returns>
    public bool Equals(KeyBinding other) => _value == other._value;

    /// <summary>Compare deux touches.</summary>
    /// <param name="obj">Objet a comparer.</param>
    /// <returns>Vrai si <paramref name="other"/> est la meme touche.</returns>
    public override bool Equals(object? obj) => obj is KeyBinding key && Equals(key);

    /// <summary>Code de la touche, forme stable pour les tables de hachage.</summary>
    /// <returns>Le code interne.</returns>
    public override int GetHashCode() => _value;

    /// <summary>Ecrit la touche sous sa forme JSON.</summary>
    /// <returns>L'entier 0 a 8, ou le nom du volant.</returns>
    public override string ToString()
    {
        if (IsGrid)
        {
            return _value.ToString(CultureInfo.InvariantCulture);
        }

        return _value == WheelLeftCode ? WheelLeftName : WheelRightName;
    }

    /// <summary>Compare deux touches.</summary>
    /// <param name="left">Premiere touche.</param>
    /// <param name="right">Seconde touche.</param>
    /// <returns>Vrai si les deux sont identiques.</returns>
    public static bool operator ==(KeyBinding left, KeyBinding right) => left.Equals(right);

    /// <summary>Compare deux touches.</summary>
    /// <param name="left">Premiere touche.</param>
    /// <param name="right">Seconde touche.</param>
    /// <returns>Vrai si les deux sont differentes.</returns>
    public static bool operator !=(KeyBinding left, KeyBinding right) => !left.Equals(right);
}

/// <summary>Lit et ecrit une <see cref="KeyBinding"/> en JSON.</summary>
/// <remarks>
/// Le schema autorise deux formes pour le meme champ : un entier 0 a 8, ou une
/// chaine. C'est exactement le genre d'union que le lecteur generique de
/// System.Text.Json ne sait pas faire tout seul.
/// </remarks>
public sealed class KeyBindingJsonConverter : JsonConverter<KeyBinding>
{
    /// <inheritdoc/>
    public override KeyBinding Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
        {
            if (reader.TryGetInt32(out int index) && index is >= 0 and < KeyBinding.GridSize)
            {
                return KeyBinding.Grid(index);
            }

            throw new JsonException(
                $"La touche {index} n'existe pas : la grille compte {KeyBinding.GridSize} touches, de 0 a " +
                $"{KeyBinding.GridSize - 1}.");
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            string? text = reader.GetString();
            if (KeyBinding.TryParse(text, out KeyBinding key))
            {
                return key;
            }

            throw new JsonException(
                $"Touche inconnue « {text} ». Attendu : un entier de 0 a {KeyBinding.GridSize - 1}, " +
                $"ou « {KeyBinding.WheelLeftName} » ou « {KeyBinding.WheelRightName} ».");
        }

        throw new JsonException($"Une touche s'ecrit par un entier ou par un nom, pas par {reader.TokenType}.");
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, KeyBinding value, JsonSerializerOptions options)
    {
        if (value.IsGrid)
        {
            writer.WriteNumberValue(int.Parse(value.ToString(), CultureInfo.InvariantCulture));
        }
        else
        {
            writer.WriteStringValue(value.ToString());
        }
    }
}
