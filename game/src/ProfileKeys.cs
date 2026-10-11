// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json;
using Boutap.Core.Audit;
using Boutap.Core.Pack;
using Godot;

namespace Boutap.Shell;

/// <summary>
/// Le profil de touches du joueur, traduit en touches Godot.
/// </summary>
/// <remarks>
/// <para>
/// Un profil declare des codes style DOM (<c>KeyA</c>, <c>BracketLeft</c>,
/// <c>Numpad1</c>) parce que c'est ce que les navigateurs et les manettes
/// nomment ainsi, et que le format doit survivre a un changement de moteur. Godot
/// nomme les siennes autrement : <c>A</c>, <c>Bracketleft</c>, <c>Kp1</c>. La
/// traduction vit ici, et nulle part ailleurs.
/// </para>
/// <para>
/// Elle passe par <see cref="Enum.Parse"/> sur le nom Godot plutot que par des
/// constantes numeriques : un nom d'enumeration est stable, une valeur de code
/// touche ne l'est pas.
/// </para>
/// <para>
/// Le profil est lu depuis le disque avec <see cref="File"/>, pas avec
/// <c>FileAccess</c> : un profil choisit son materiel, il n'a rien a voir avec
/// les ressources du paquet, et <c>res://</c> ne verrait rien d'un fichier pose
/// a cote de l'executable.
/// </para>
/// </remarks>
public static class ProfileKeys
{
    /// <summary>Nom du profil charge si le joueur n'en a choisi aucun.</summary>
    public const string DefaultProfileName = "default-9";

    private static readonly Dictionary<string, string> GodotNames = new(StringComparer.Ordinal)
    {
        ["BracketLeft"] = "Bracketleft",
        ["BracketRight"] = "Bracketright",
        ["Space"] = "Space",
        ["Enter"] = "Enter",
        ["Tab"] = "Tab",
        ["Backspace"] = "Backspace",
        ["ShiftLeft"] = "Shift",
        ["ShiftRight"] = "Shift",
        ["ControlLeft"] = "Ctrl",
        ["ControlRight"] = "Ctrl",
        ["AltLeft"] = "Alt",
        ["AltRight"] = "Alt",
        ["Comma"] = "Comma",
        ["Period"] = "Period",
        ["Slash"] = "Slash",
        ["Semicolon"] = "Semicolon",
        ["Quote"] = "Apostrophe",
        ["Minus"] = "Minus",
        ["Equal"] = "Equal",
        ["Backslash"] = "Backslash",
        ["Backquote"] = "QuoteLeft",
        ["ArrowUp"] = "Up",
        ["ArrowDown"] = "Down",
        ["ArrowLeft"] = "Left",
        ["ArrowRight"] = "Right",
    };

    /// <summary>Traduit un code DOM en touche Godot.</summary>
    /// <param name="domCode">Code declare par le profil, par exemple <c>KeyA</c>.</param>
    /// <returns>La touche, ou <see langword="null"/> si le code est inconnu.</returns>
    public static Key? CodeOf(string? domCode)
    {
        if (string.IsNullOrWhiteSpace(domCode))
        {
            return null;
        }

        string name = GodotName(domCode);

        // Le nom peut ne pas exister dans la version de Godot embarquee : un
        // profil ne doit pas empecher le jeu de demarrer pour une touche que
        // l'on ne sait pas nommer.
        try
        {
            return Enum.Parse(typeof(Key), name, ignoreCase: true) is Key parsed
                ? parsed
                : null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Charge un profil et rend la table touche vers touche de grille.</summary>
    /// <param name="path">Chemin du fichier de profil.</param>
    /// <returns>
    /// La table, ou <see langword="null"/> si le fichier est absent ou illisible.
    /// </returns>
    public static IReadOnlyDictionary<Key, KeyBinding>? TryLoad(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        PlayerProfile? profile;
        try
        {
            profile = JsonSerializer.Deserialize<PlayerProfile>(File.ReadAllText(path));
        }
        catch (JsonException)
        {
            GD.PrintErr($"Profil illisible, on garde la disposition par defaut : {path}");
            return null;
        }
        catch (IOException)
        {
            return null;
        }

        if (profile?.Buttons is null)
        {
            return null;
        }

        var map = new Dictionary<Key, KeyBinding>();
        foreach ((string name, ProfileButton? button) in profile.Buttons)
        {
            if (button is null || !string.Equals(button.Kind, "key", StringComparison.Ordinal))
            {
                continue;
            }

            KeyBinding? binding = BindingOf(name);
            if (binding is null)
            {
                continue;
            }

            if (CodeOf(button.Code) is Key code && !map.ContainsKey(code))
            {
                map[code] = binding.Value;
            }
        }

        return map.Count > 0 ? map : null;
    }

    /// <summary>La table de repli, identique a <c>profiles/default-9.json</c>.</summary>
    /// <remarks>
    /// Ce repli existe pour qu'un jeu exporte, sans le depot a cote de lui, reste
    /// jouable. Il duplique le profil livre : c'est assume jusqu'a ce que les
    /// profils voyagent dans le paquet (slice A4), et c'est note dans le
    /// CHANGELOG. Un joueur qui modifie le fichier du depot retrouve son choix
    /// des que le profil est trouve.
    /// </remarks>
    public static IReadOnlyDictionary<Key, KeyBinding> BuiltIn()
    {
        var map = new Dictionary<Key, KeyBinding>
        {
            [Key.Bracketleft] = KeyBinding.Grid(0),
            [Key.A] = KeyBinding.Grid(1),
            [Key.Q] = KeyBinding.Grid(2),
            [Key.W] = KeyBinding.Grid(3),
            [Key.S] = KeyBinding.Grid(4),
            [Key.E] = KeyBinding.Grid(5),
            [Key.D] = KeyBinding.Grid(6),
            [Key.R] = KeyBinding.Grid(7),
            [Key.F] = KeyBinding.Grid(8),
            [Key.Z] = KeyBinding.WheelLeft,
            [Key.X] = KeyBinding.WheelRight,
        };

        return map;
    }

    private static KeyBinding? BindingOf(string name) => name switch
    {
        "WHEEL_L" => KeyBinding.WheelLeft,
        "WHEEL_R" => KeyBinding.WheelRight,
        _ when name.Length == 2 && name[0] == 'T' && char.IsDigit(name[1])
            && int.TryParse(name[1..], out int index) && index >= 0 && index < KeyBinding.GridSize
            => KeyBinding.Grid(index),
        _ => null,
    };

    private static string GodotName(string domCode)
    {
        if (GodotNames.TryGetValue(domCode, out string? named))
        {
            return named;
        }

        if (domCode.StartsWith("Key", StringComparison.Ordinal) && domCode.Length == 4)
        {
            return domCode[3..];
        }

        if (domCode.StartsWith("Digit", StringComparison.Ordinal) && domCode.Length == 6)
        {
            return domCode[5..];
        }

        if (domCode.StartsWith("Numpad", StringComparison.Ordinal) && domCode.Length == 7)
        {
            return "Kp" + domCode[6..];
        }

        return domCode;
    }
}
