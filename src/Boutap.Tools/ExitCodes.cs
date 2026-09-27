// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Boutap.Tools;

/// <summary>Codes de sortie de l'outil, stables d'une version a l'autre.</summary>
/// <remarks>
/// Une chaine d'appel — un script, un fichier de travail Git, une CI — doit
/// pouvoir distinguer « le fichier est invalide » de « l'outil a plante ».
/// </remarks>
public static class ExitCodes
{
    /// <summary>Tout s'est bien passe.</summary>
    public const int Success = 0;

    /// <summary>L'appel a echoue pour une raison inattendue.</summary>
    public const int Failure = 1;

    /// <summary>La ligne de commande est mal formee.</summary>
    public const int UsageError = 2;

    /// <summary>Le contenu lu est invalide. C'est le seul code qui fait foi.</summary>
    public const int Invalid = 3;
}
