// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Les dix regles de lisibilite : wiki generateur.md §6.1.
//
// Ce sont des seuils en millisecondes, pas des constantes physiques. La
// specification le dit elle-meme : ils viennent de la litterature sur la
// dexterite et seront calibres par le test utilisateur de S7.8. Ils sont donc
// regroupes ici, nommes, et aucun n'est ecrit en dur ailleurs dans le code.

namespace Boutap.Gen.Compose;

/// <summary>Les dix seuils de lisibilite, en secondes sauf mention contraire.</summary>
public static class ReadabilityRules
{
    /// <summary>L1 : deux notes sur la meme touche, en millisecondes.</summary>
    public const double SameKeySeconds = 0.090;

    /// <summary>L2 : deux notes de touches differentes, en millisecondes.</summary>
    public const double DifferentKeySeconds = 0.055;

    /// <summary>L3 : nombre maximum de notes simultanees.</summary>
    public const int MaxSimultaneous = 4;

    /// <summary>L4 : deux notes de touches voisines, en millisecondes.</summary>
    public const double AdjacentKeySeconds = 0.025;

    /// <summary>L5 : fenetre et maximum de notes.</summary>
    public const double ShortWindowSeconds = 0.100;

    /// <summary>Nombre de notes maximum dans <see cref="ShortWindowSeconds"/>.</summary>
    public const int MaxPerShortWindow = 4;

    /// <summary>L6 : fenetre et maximum de notes.</summary>
    public const double LongWindowSeconds = 1.0;

    /// <summary>Nombre de notes maximum dans <see cref="LongWindowSeconds"/>.</summary>
    public const int MaxPerLongWindow = 16;

    /// <summary>L7 : distance au debut et a la fin d'un tenu, en secondes.</summary>
    public const double HoldClearanceSeconds = 0.040;

    /// <summary>L8 : notes maximum par rafale.</summary>
    public const int MaxPerBurst = 6;

    /// <summary>L9 : duree du silence maximal, en temps.</summary>
    public const double MaxSilenceBeats = 4.0;

    /// <summary>L9 : densite, en notes par seconde, au-dessus de laquelle la zone est dite dense.</summary>
    /// <remarks>
    /// La regle parle de « zone d'intensite haute » sans le definir. On prend
    /// un huitieme de la limite de L6 : c'est une zone ou le joueur n'a pas le
    /// temps de respirer, et c'est la que l'absence subite de notes ressemble
    /// a une erreur plutot qu'a un silence voulu.
    /// </remarks>
    public const double DenseNotesPerSecond = MaxPerLongWindow / LongWindowSeconds / 8.0;
}
