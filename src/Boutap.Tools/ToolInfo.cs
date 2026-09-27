// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Reflection;

namespace Boutap.Tools;

/// <summary>Identite de l'outil, en un seul endroit.</summary>
/// <remarks>
/// Un nom, une version et une licence ecrits a trois endroits finissent
/// toujours par diverger. Tout part d'ici.
/// </remarks>
public static class ToolInfo
{
    /// <summary>Nom de l'executable, tel qu'on le tape.</summary>
    public const string Name = "boutap";

    /// <summary>Description en une ligne.</summary>
    public const string Description =
        "Outils de preparation des packs .btp : audit, validation, lecture, mesure.";

    /// <summary>Identifiant SPDX de la licence du code.</summary>
    public const string License = "AGPL-3.0-or-later";

    /// <summary>Depot du projet.</summary>
    public const string RepositoryUrl = "https://github.com/boutap/boutap";

    /// <summary>Version de l'outil, lue dans l'assembly.</summary>
    public static string Version
    {
        get
        {
            Assembly assembly = typeof(ToolInfo).Assembly;
            AssemblyInformationalVersionAttribute? informational =
                assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            if (informational is not null)
            {
                // Le SDK ajoute « + sha » a la version informationnelle.
                int plus = informational.InformationalVersion.IndexOf('+', StringComparison.Ordinal);
                return plus < 0
                    ? informational.InformationalVersion
                    : informational.InformationalVersion[..plus];
            }

            return assembly.GetName().Version?.ToString() ?? "0.0.0";
        }
    }
}
