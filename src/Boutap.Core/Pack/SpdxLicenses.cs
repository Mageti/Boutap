// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.RegularExpressions;

namespace Boutap.Core.Pack;

/// <summary>Verdict sur une licence de contenu, apres confrontation a la liste blanche.</summary>
/// <param name="State">Le verdict.</param>
/// <param name="Reason">Pourquoi, en une phrase.</param>
public readonly record struct LicenseVerdict(LicenseState State, string Reason)
{
    /// <summary>Refuse : la licence n'est pas dans la liste blanche.</summary>
    public bool IsRefused => State == LicenseState.Refused;

    /// <summary>Accepte avec reserve : l'obligation de partage en lecture.</summary>
    public bool HasWarning => State == LicenseState.AcceptedWithWarning;
}

/// <summary>Verdict possibles pour une licence de contenu.</summary>
public enum LicenseState
{
    /// <summary>Acceptable telle quelle.</summary>
    Accepted = 0,

    /// <summary>Acceptable, mais avec une obligation a respecter par le lecteur.</summary>
    AcceptedWithWarning = 1,

    /// <summary>Hors liste blanche.</summary>
    Refused = 2,
}

/// <summary>
/// Ce que le projet accepte comme licence de <em>contenu</em> — musique, chartes,
/// samples. Ce n'est pas la meme question que la licence du code, qui est AGPL
/// sans negotiation.
/// </summary>
/// <remarks>
/// <para>
/// La liste est fermee, et c'est un choix de risque. Un lecteur qui sait
/// relire une licence arbitraire finit par relire, dans dix ans, une licence
/// incompatible avec ses usages. On refuse donc ce qu'on ne sait pas expliquer,
/// et on exige que le lecteur puisse afficher le texte.
/// </para>
/// <para>
/// <c>CC-BY-SA-4.0</c> est accepte avec avertissement parce qu'il est
/// techniquement jouable — il oblige a republier sous la meme licence une
/// modification de la partition, ce qui ne touche pas au code, lui est AGPL —
/// mais parce que la plupart des gens ne savent pas.
/// </para>
/// </remarks>
public static partial class SpdxLicenses
{
    /// <summary>Licences acceptees sans reserve.</summary>
    public static IReadOnlyList<string> Accepted { get; } = new[]
    {
        "CC0-1.0",
        "CC-BY-4.0",
        "ODbL-1.0",
    };

    /// <summary>Licences acceptees avec avertissement.</summary>
    public static IReadOnlyList<string> AcceptedWithWarning { get; } = new[]
    {
        "CC-BY-SA-4.0",
    };

    /// <summary>Toutes les licences tolerees, avec leur verdict.</summary>
    public static IReadOnlyDictionary<string, LicenseState> Allowed { get; } = Build();

    /// <summary>Applique la liste blanche.</summary>
    public static LicenseVerdict Evaluate(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return new LicenseVerdict(
                LicenseState.Refused,
                "content_license est obligatoire : sans licence, pas de redistribution possible.");
        }

        string id = expression.Trim();
        if (!IdentifierPattern().IsMatch(id))
        {
            return new LicenseVerdict(
                LicenseState.Refused,
                $"« {id} » n'est pas un identifiant SPDX. Exemple attendu : « CC-BY-4.0 ».");
        }

        if (Allowed.TryGetValue(id, out LicenseState state))
        {
            return state == LicenseState.AcceptedWithWarning
                ? new LicenseVerdict(
                    state,
                    "CC-BY-SA-4.0 impose de republier toute modification de la partition sous la "
                    + "meme licence. Le code du lecteur reste sous AGPL, mais un lecteur qui "
                    + "modifie et redistribue la partition doit le savoir.")
                : new LicenseVerdict(state, $"{id} est acceptee.");
        }

        return new LicenseVerdict(
            LicenseState.Refused,
            $"« {id} » n'est pas dans la liste blanche. Attendu : "
            + string.Join(", ", Accepted)
            + ", ou "
            + string.Join(", ", AcceptedWithWarning)
            + " avec avertissement.");
    }

    private static Dictionary<string, LicenseState> Build()
    {
        Dictionary<string, LicenseState> map = new(StringComparer.Ordinal);
        foreach (string id in Accepted)
        {
            map[id] = LicenseState.Accepted;
        }

        foreach (string id in AcceptedWithWarning)
        {
            map[id] = LicenseState.AcceptedWithWarning;
        }

        return map;
    }

    /// <summary>
    /// Un identifiant SPDX de licence, tel que <c>CC-BY-4.0</c> ou
    /// <c>ODbL-1.0</c>. Le format n'accepte qu'un identifiant simple, pas une
    /// expression composee : un pack dont le contenu est sous deux licences doit
    /// le dire autrement, et c'est un probleme de fond, pas de syntaxe.
    /// </summary>
    [GeneratedRegex(@"^[A-Za-z0-9.+-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierPattern();
}
