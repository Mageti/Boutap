// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json;
using System.Text.RegularExpressions;
using Boutap.Core.Pack;

namespace Boutap.Core.Audit;

/// <summary>Verifie qu'un profil de touches respecte le format.</summary>
/// <remarks>
/// <para>
/// Le format de profil est le seul du depot qui n'a pas de schema JSON. Ses
/// regles sont donc ici, en code, et l'audit s'appuie dessus. Elles sont
/// volontairement minces : tout ce que le format ne dit pas, l'audit ne le
/// devine pas.
/// </para>
/// </remarks>
public static partial class ProfileValidator
{
    /// <summary>Version de format acceptee.</summary>
    public const string SupportedFormatVersion = "1.0";

    /// <summary>Bornes du decalage de latence, en millisecondes.</summary>
    public static IReadOnlyList<int> LatencyRangeMilliseconds { get; } = new[] { -500, 500 };

    /// <summary>Zone morte maximale, en valeur absolue.</summary>
    public const double MaxDeadzone = 0.5;

    /// <summary>Les neuf touches de la grille 3x3, dans l'ordre de la grille.</summary>
    public static IReadOnlyList<string> GridKeys { get; } =
        Enumerable.Range(0, Pack.KeyBinding.GridSize).Select(i => $"T{i}").ToArray();

    /// <summary>Les deux noms de repli de volant, dans <c>buttons</c>.</summary>
    public static IReadOnlyList<string> WheelButtonKeys { get; } = new[] { "WHEEL_L", "WHEEL_R" };

    /// <summary>Les deux cotes de volant, dans <c>wheels</c>.</summary>
    public static IReadOnlyList<string> WheelSides { get; } = new[] { "L", "R" };

    /// <summary>Verifie un profil deja lu.</summary>
    /// <param name="profile">Le profil.</param>
    /// <param name="expectedName">
    /// Nom attendu, generalement celui du fichier sans extension. Vrai ou
    /// <see langword="null"/> pour ne pas exiger la correspondance.
    /// </param>
    /// <returns>Tous les constats.</returns>
    public static ValidationResult Validate(PlayerProfile? profile, string? expectedName = null)
    {
        ValidationResult result = new();

        if (profile is null)
        {
            return result.Error(AuditCodes.ProfileNotJson, "Le profil est vide ou illisible.");
        }

        if (profile.Profile is not string formatVersion)
        {
            return result.Error(AuditCodes.ProfileMissingField, "« profile » est absent.", "profile");
        }

        if (!FormatVersionPattern().IsMatch(formatVersion))
        {
            result.Error(
                AuditCodes.ProfileBadValue,
                $"« {formatVersion} » n'a pas la forme d'une version de format « M.m ».",
                "profile");
        }
        else if (formatVersion != SupportedFormatVersion)
        {
            result.Error(
                AuditCodes.ProfileBadValue,
                $"« {formatVersion} » n'est pas une version de format acceptee ; attendu « {SupportedFormatVersion} ».",
                "profile");
        }

        if (profile.Name is not string name)
        {
            result.Error(AuditCodes.ProfileMissingField, "« name » est absent.", "name");
        }
        else if (expectedName is not null && name != expectedName)
        {
            result.Error(
                AuditCodes.ProfileNameMismatch,
                $"Le champ « name » vaut « {name} » alors que le fichier s'appelle « {expectedName} ».",
                "name");
        }

        if (profile.LatencyMs is int latency && !InLatencyRange(latency))
        {
            result.Error(
                AuditCodes.ProfileBadValue,
                $"« latency_ms » vaut {latency} ; attendu entre {LatencyRangeMilliseconds[0]} et {LatencyRangeMilliseconds[1]}.",
                "latency_ms");
        }

        CheckButtons(profile.Buttons, result);
        CheckWheels(profile.Wheels, result);

        if (profile.Players is int players && players < 1)
        {
            result.Error(
                AuditCodes.ProfileBadValue,
                $"« players » vaut {players} ; un profil decrit au moins un joueur.",
                "players");
        }

        return result;
    }

    /// <summary>Verifie un profil depuis son texte JSON.</summary>
    /// <param name="json">Texte du fichier.</param>
    /// <param name="expectedName">Nom attendu du profil, ou <see langword="null"/>.</param>
    /// <param name="result">Constats deja accumules, pour une seule passe.</param>
    /// <param name="profile">Le profil lu, si la lecture a reussi.</param>
    /// <returns>Vrai si le JSON a pu etre lu.</returns>
    public static bool TryRead(string json, string? expectedName, ValidationResult result, out PlayerProfile? profile)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(result);
        _ = expectedName;

        try
        {
            profile = JsonSerializer.Deserialize<PlayerProfile>(json, PackJson.ReadOptions);
            return true;
        }
        catch (JsonException ex)
        {
            profile = null;
            result.Error(AuditCodes.ProfileNotJson, $"Le profil n'est pas du JSON valide : {ex.Message}");
            return false;
        }
    }

    /// <summary>Vrai si un decalage de latence est dans les bornes du format.</summary>
    /// <param name="milliseconds">Decalage en millisecondes.</param>
    /// <returns>Vrai si la valeur est acceptable.</returns>
    public static bool InLatencyRange(int milliseconds) =>
        milliseconds >= LatencyRangeMilliseconds[0] && milliseconds <= LatencyRangeMilliseconds[1];

    private static void CheckButtons(IReadOnlyDictionary<string, ProfileButton>? buttons, ValidationResult result)
    {
        if (buttons is null)
        {
            result.Error(AuditCodes.ProfileMissingField, "« buttons » est absent.", "buttons");
            return;
        }

        foreach (string gridKey in GridKeys)
        {
            if (!buttons.ContainsKey(gridKey))
            {
                result.Error(
                    AuditCodes.ProfileMissingButton,
                    $"La touche « {gridKey} » n'est pas declaree.",
                    ValidationResult.JsonPointerField("buttons", gridKey));
            }
        }

        HashSet<string> known = new(GridKeys, StringComparer.Ordinal);
        foreach (string wheel in WheelButtonKeys)
        {
            known.Add(wheel);
        }

        foreach (string name in buttons.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            string pointer = ValidationResult.JsonPointerField("buttons", name);

            if (!known.Contains(name))
            {
                result.Error(
                    AuditCodes.ProfileUnknownButton,
                    $"« {name} » n'est pas une touche du format ; attendu T0 a T8, WHEEL_L ou WHEEL_R.",
                    pointer);
            }

            ProfileButton? button = buttons[name];
            CheckKind(button?.Kind, ProfileButton.ButtonKinds, pointer, result, "une touche");
            if (button is not null && button.Kind is "key" or "gamepad_button" && string.IsNullOrWhiteSpace(button.Code))
            {
                result.Error(
                    AuditCodes.ProfileMissingField,
                    "Une touche de ce type doit nommer un code de touche ou de bouton.",
                    pointer);
            }
        }
    }

    private static void CheckWheels(IReadOnlyDictionary<string, ProfileWheel>? wheels, ValidationResult result)
    {
        if (wheels is null)
        {
            return;
        }

        foreach (string side in WheelSides)
        {
            if (!wheels.TryGetValue(side, out ProfileWheel? wheel))
            {
                continue;
            }

            string pointer = ValidationResult.JsonPointerField("wheels", side);
            CheckKind(wheel?.Kind, ProfileButton.Kinds, pointer, result, "un volant");

            if (wheel is null)
            {
                continue;
            }

            if (wheel.Kind == "axis")
            {
                CheckAnalog(wheel, pointer, result);
            }
        }

        foreach (string name in wheels.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            if (!WheelSides.Contains(name, StringComparer.Ordinal))
            {
                result.Error(
                    AuditCodes.ProfileUnknownButton,
                    $"« {name} » n'est pas un cote de volant du format ; attendu L ou R.",
                    ValidationResult.JsonPointerField("wheels", name));
            }
        }
    }

    private static void CheckAnalog(ProfileWheel wheel, string jsonPointer, ValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(wheel.Code))
        {
            result.Error(AuditCodes.ProfileMissingField, "Un volant analogique doit nommer un code d'axe.", jsonPointer);
        }

        if (wheel.Invert is not bool)
        {
            result.Error(
                AuditCodes.ProfileMissingField,
                "Un volant analogique doit dire « invert », meme a faux : c'est l'absence d'inversion qui est declaree, pas l'absence de decision.",
                jsonPointer);
        }

        double deadzone = 0.0;
        if (wheel.Deadzone is not double declaredDeadzone)
        {
            result.Error(AuditCodes.ProfileMissingField, "« deadzone » est absent.", jsonPointer);
        }
        else
        {
            deadzone = Math.Abs(declaredDeadzone);
            if (deadzone > MaxDeadzone)
            {
                result.Error(
                    AuditCodes.ProfileBadValue,
                    $"« deadzone » vaut {declaredDeadzone} ; au-dela de {MaxDeadzone}, le volant ne sort jamais de sa zone morte.",
                    jsonPointer);
            }
        }

        if (wheel.Smoothing is not double smoothing)
        {
            result.Error(AuditCodes.ProfileMissingField, "« smoothing » est absent.", jsonPointer);
        }
        else if (smoothing is < 0.0 or > 1.0)
        {
            result.Error(
                AuditCodes.ProfileBadValue,
                $"« smoothing » vaut {smoothing} ; attendu dans [0 ; 1].",
                jsonPointer);
        }

        if (wheel.Trigger is not double trigger)
        {
            result.Error(AuditCodes.ProfileMissingField, "« trigger » est absent.", jsonPointer);
        }
        else if (trigger <= deadzone)
        {
            result.Error(
                AuditCodes.ProfileBadValue,
                $"« trigger » vaut {trigger} et « deadzone » vaut {deadzone} ; le seuil doit etre au-dessus de la zone morte, sinon l'axe se declenche sans bouger.",
                jsonPointer);
        }
    }

    private static void CheckKind(
        string? kind,
        IReadOnlyList<string> accepted,
        string jsonPointer,
        ValidationResult result,
        string subject)
    {
        if (kind is null)
        {
            result.Error(AuditCodes.ProfileMissingField, "« type » est absent.", jsonPointer);
            return;
        }

        if (!accepted.Contains(kind, StringComparer.Ordinal))
        {
            result.Error(
                AuditCodes.ProfileBadValue,
                $"« {kind} » n'est pas un type accepte pour {subject} ; attendu {string.Join(", ", accepted)}.",
                jsonPointer);
            return;
        }

    }

    [GeneratedRegex(@"^\d+\.\d+$", RegexOptions.CultureInvariant)]
    private static partial Regex FormatVersionPattern();
}
