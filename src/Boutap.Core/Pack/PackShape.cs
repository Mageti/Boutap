// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Boutap.Core.Pack;

/// <summary>
/// Les noms de champs du format, ecrits une fois. Le validateur s'en sert pour
/// verifier qu'aucun champ inconnu ne s'est glisse dans un document, et le
/// test de contrat s'en sert pour verifier que le modele et le schema
/// passent en revue la meme liste.
/// </summary>
public static class PackShape
{
    /// <summary>Champs du manifeste.</summary>
    public static IReadOnlySet<string> ManifestFields { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "schema", "id", "title", "artist", "audio", "analysis", "generator",
        "charts", "content_license", "min_player_version",
    };

    /// <summary>Champs de <c>audio</c>.</summary>
    public static IReadOnlySet<string> AudioFields { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "path", "sha256", "duration_seconds", "pre_skip_seconds", "gapless_metadata",
    };

    /// <summary>Champs de <c>analysis</c>.</summary>
    public static IReadOnlySet<string> AnalysisFields { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "duration_seconds", "bpm", "bpm_confidence", "key", "key_confidence",
    };

    /// <summary>Champs de <c>generator</c>.</summary>
    public static IReadOnlySet<string> GeneratorFields { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "name", "version", "params", "seed",
    };

    /// <summary>Champs d'une entree de <c>charts</c>.</summary>
    public static IReadOnlySet<string> ChartEntryFields { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "level", "file", "note_count", "duration_seconds", "nps", "peak_load", "rating",
    };

    /// <summary>Champs de <c>generator</c> dans une chart, qui n'a pas de <c>params</c>.</summary>
    public static IReadOnlySet<string> ChartGeneratorFields { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "name", "version", "seed",
    };

    /// <summary>Champs d'une chart.</summary>
    public static IReadOnlySet<string> ChartFields { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "schema", "level", "audio_sha256", "offset_seconds", "generator", "notes",
    };

    /// <summary>Champs d'une note.</summary>
    public static IReadOnlySet<string> NoteFields { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "t", "k", "d", "v", "w", "s",
    };
}
