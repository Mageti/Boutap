// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Boutap.Core.Common;

namespace Boutap.Core.Pack;

/// <summary>L'audio contenu dans le pack, et la ou il se trouve.</summary>
public sealed record AudioRef
{
    /// <summary>Chemin de l'audio, relatif a la racine du pack.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("path")]
    public string? Path { get; init; }

    /// <summary>SHA-256 de l'audio <em>decode</em>, 64 hex minuscules.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("sha256")]
    public string? Sha256 { get; init; }

    /// <summary>Duree de l'audio decode, en secondes.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("duration_seconds")]
    public double? DurationSeconds { get; init; }

    /// <summary>Silence en tete, en secondes, pour caler le debut utile.</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("pre_skip_seconds")]
    public double? PreSkipSeconds { get; init; }

    /// <summary>Balise de lecture sans coupure trouvee dans le fichier.</summary>
    [JsonPropertyOrder(4)]
    [JsonPropertyName("gapless_metadata")]
    public GaplessTag? GaplessMetadata { get; init; }
}

/// <summary>Ce que l'analyse a reussi a mesurer sur l'audio.</summary>
public sealed record AnalysisInfo
{
    /// <summary>Duree mesuree. Doit egaler celle de l'audio a 5 ms pres.</summary>
    [JsonPropertyOrder(5)]
    [JsonPropertyName("duration_seconds")]
    public double? DurationSeconds { get; init; }

    /// <summary>Tempo mesure, en battements par minute.</summary>
    [JsonPropertyOrder(6)]
    [JsonPropertyName("bpm")]
    public double? Bpm { get; init; }

    /// <summary>Confiance dans le tempo, de 0 a 1.</summary>
    [JsonPropertyOrder(7)]
    [JsonPropertyName("bpm_confidence")]
    public double? BpmConfidence { get; init; }

    /// <summary>Tonalite estimee, ecrite comme elle a ete reconnue.</summary>
    [JsonPropertyOrder(8)]
    [JsonPropertyName("key")]
    public KeySignature? Key { get; init; }

    /// <summary>Confiance dans la tonalite, de 0 a 1.</summary>
    [JsonPropertyOrder(9)]
    [JsonPropertyName("key_confidence")]
    public double? KeyConfidence { get; init; }
}

/// <summary>Le generateur, avec la trace des parametres qu'il a utilises.</summary>
public sealed record GeneratorInfo
{
    /// <summary>Nom du generateur, tel qu'il apparait dans le credit.</summary>
    [JsonPropertyOrder(10)]
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>Version du generateur.</summary>
    [JsonPropertyOrder(11)]
    [JsonPropertyName("version")]
    public string? Version { get; init; }

    /// <summary>
    /// Parametres utilises. Objet libre et non contractuel : sa forme peut
    /// changer sans casser le format, c'est une trace, pas une interface.
    /// </summary>
    [JsonPropertyOrder(12)]
    [JsonPropertyName("params")]
    public JsonObject? Params { get; init; }

    /// <summary>Graine. Derivee du contenu decode de l'audio, jamais de son nom.</summary>
    [JsonPropertyOrder(13)]
    [JsonPropertyName("seed")]
    public ulong? Seed { get; init; }
}

/// <summary>Ce qu'un niveau contient, dans le manifeste.</summary>
public sealed record ChartEntry
{
    /// <summary>Niveau.</summary>
    [JsonPropertyOrder(14)]
    [JsonPropertyName("level")]
    public ChartLevel? Level { get; init; }

    /// <summary>Chemin de la chart dans le pack, forme <c>charts/xxx.json</c>.</summary>
    [JsonPropertyOrder(15)]
    [JsonPropertyName("file")]
    public string? File { get; init; }

    /// <summary>Nombre de notes. Doit correspondre au fichier reel.</summary>
    [JsonPropertyOrder(16)]
    [JsonPropertyName("note_count")]
    public int? NoteCount { get; init; }

    /// <summary>Duree du niveau, en secondes.</summary>
    [JsonPropertyOrder(17)]
    [JsonPropertyName("duration_seconds")]
    public double? DurationSeconds { get; init; }

    /// <summary>Notes par seconde, sur la fenetre glissante la plus dense.</summary>
    [JsonPropertyOrder(18)]
    [JsonPropertyName("nps")]
    public double? Nps { get; init; }

    /// <summary>Charge de jeu, de 0 a 10.</summary>
    [JsonPropertyOrder(19)]
    [JsonPropertyName("peak_load")]
    public double? PeakLoad { get; init; }

    /// <summary>Difficulte declaree, de 1 a 20, en nombre entier.</summary>
    [JsonPropertyOrder(20)]
    [JsonPropertyName("rating")]
    public int? Rating { get; init; }
}

/// <summary>Le manifeste d'un pack : ce qu'un lecteur sait avant de jouer.</summary>
public sealed record Manifest
{
    /// <summary>Valeur constante : <c>boutap/pack-manifest/1</c>.</summary>
    [JsonPropertyOrder(21)]
    [JsonPropertyName("schema")]
    public string? Schema { get; init; }

    /// <summary>Identifiant stable, en minuscules : sert de cle de cache.</summary>
    [JsonPropertyOrder(22)]
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>Titre affiche.</summary>
    [JsonPropertyOrder(23)]
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    /// <summary>Artiste. Vide si l'auteur ne le precise pas.</summary>
    [JsonPropertyOrder(24)]
    [JsonPropertyName("artist")]
    public string? Artist { get; init; }

    /// <summary>L'audio du pack.</summary>
    [JsonPropertyOrder(25)]
    [JsonPropertyName("audio")]
    public AudioRef? Audio { get; init; }

    /// <summary>Ce que l'analyse a mesure.</summary>
    [JsonPropertyOrder(26)]
    [JsonPropertyName("analysis")]
    public AnalysisInfo? Analysis { get; init; }

    /// <summary>Le generateur et sa graine.</summary>
    [JsonPropertyOrder(27)]
    [JsonPropertyName("generator")]
    public GeneratorInfo? Generator { get; init; }

    /// <summary>Les niveaux du pack, de un a trois.</summary>
    [JsonPropertyOrder(28)]
    [JsonPropertyName("charts")]
    public IReadOnlyList<ChartEntry>? Charts { get; init; }

    /// <summary>Licence du contenu, en expression SPDX.</summary>
    [JsonPropertyOrder(29)]
    [JsonPropertyName("content_license")]
    public string? ContentLicense { get; init; }

    /// <summary>Version minimale du lecteur capable de jouer ce pack.</summary>
    [JsonPropertyOrder(30)]
    [JsonPropertyName("min_player_version")]
    public SemanticVersion? MinPlayerVersion { get; init; }
}
