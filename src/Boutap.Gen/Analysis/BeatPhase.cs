// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Phase rythmique d'un battement : wiki generateur.md §4.9.

namespace Boutap.Gen.Analysis;

/// <summary>Ce qu'un battement contient, once decoupe en phases.</summary>
/// <param name="StartSeconds">Debut du battement, en secondes absolues.</param>
/// <param name="LengthSeconds">Duree du battement, en secondes.</param>
/// <param name="Phases">L'energie de chacune des phases.</param>
/// <param name="StrongestPhase">Index de la phase la plus forte.</param>
public sealed record BeatPhase(
    double StartSeconds,
    double LengthSeconds,
    IReadOnlyList<double> Phases,
    int StrongestPhase)
{
    /// <summary>Le moment de la phase forte, en secondes absolues.</summary>
    /// <remarks>
    /// C'est le point que la generation vise en priorite : dans un morceau
    /// ou la caisse claire tombe a contretemps, c'est la que la note doit
    /// tomber aussi.
    /// </remarks>
    public double StrongestTimeSeconds => StartSeconds + (LengthSeconds * StrongestPhase / Phases.Count);
}
