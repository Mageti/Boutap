// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Boutap.Core.Pack;

/// <summary>
/// Les trois niveaux de difficulte. Le nom est la donnee contractuelle : c'est
/// lui que le manifeste et la chart ecrivent, et celui que les profils
/// referencent. Le nom de fichier interne, lui, n'est qu'un detail.
/// </summary>
public enum ChartLevel
{
    /// <summary>Premier niveau, le plus simple.</summary>
    Berceau = 0,

    /// <summary>Niveau intermediaire.</summary>
    Ronde = 1,

    /// <summary>Niveau le plus exigeant.</summary>
    Cascade = 2,
}

/// <summary>Conversions entre <see cref="ChartLevel"/> et sa forme textuelle.</summary>
public static class ChartLevels
{
    /// <summary>Tous les niveaux, du plus simple au plus exigeant.</summary>
    public static IReadOnlyList<ChartLevel> All { get; } =
        new[] { ChartLevel.Berceau, ChartLevel.Ronde, ChartLevel.Cascade };

    /// <summary>Nom interne utilise pour le nom de fichier.</summary>
    public static string FileNameOf(this ChartLevel level) => level switch
    {
        ChartLevel.Berceau => "berceau",
        ChartLevel.Ronde => "ronde",
        ChartLevel.Cascade => "cascade",
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Niveau inconnu."),
    };

    /// <summary>Convertit un nom de schema en niveau.</summary>
    public static bool TryParse(string? name, out ChartLevel level)
    {
        switch (name)
        {
            case "berceau":
                level = ChartLevel.Berceau;
                return true;
            case "ronde":
                level = ChartLevel.Ronde;
                return true;
            case "cascade":
                level = ChartLevel.Cascade;
                return true;
            default:
                level = default;
                return false;
        }
    }

    /// <summary>Convertit un nom de schema en niveau, ou leve.</summary>
    public static ChartLevel Parse(string? name)
    {
        if (!TryParse(name, out ChartLevel level))
        {
            throw new FormatException($"« {name} » n'est pas un niveau : attendu berceau, ronde ou cascade.");
        }

        return level;
    }
}
