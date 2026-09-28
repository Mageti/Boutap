// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Boutap.Core.Pack;

namespace Boutap.Core.Audit;

/// <summary>Ce qu'un audit a regarde, et ce qu'il a trouve.</summary>
/// <param name="Kind">
/// Sujet audite : <c>schema</c>, <c>profile</c> ou <c>pack</c>. Le genre permet
/// a l'appelant de trier et a la sortie d'etre lisible.
/// </param>
/// <param name="Label">Chemin du sujet, relatif a la racine auditee.</param>
/// <param name="Result">Constats.</param>
public sealed record AuditSubject(string Kind, string Label, ValidationResult Result)
{
    /// <summary>Vrai si le sujet est conforme.</summary>
    public bool IsClean => Result.IsValid;

    /// <summary>
    /// Vrai si le sujet ne doit pas etre valide, mais casser comme son index
    /// l'annonce.
    /// </summary>
    /// <remarks>
    /// Sans cette distinction, un rapport d'audit ne sait pas distinguer un
    /// pack de test qui fait son travail d'un pack publie qui ne marche pas.
    /// </remarks>
    public bool Expected { get; init; }
}
