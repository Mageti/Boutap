// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json;
using System.Text.Json.Nodes;
using Boutap.Core.Common;
using Boutap.Core.Determinism;

namespace Boutap.Core.Pack;

/// <summary>
/// Verifie qu'un pack respecte le format. Toutes les regles sont ici, et nulle
/// part ailleurs : le lecteur ne juge pas, l'ecrivain ne verifie pas.
/// </summary>
/// <remarks>
/// <para>
/// Le validateur n'arrete jamais au premier probleme. Corriger un pack demande
/// de les voir tous, et un validateur qui s'arrete a la premiere erreur oblige a
/// autant d'executions que de problemes.
/// </para>
/// <para>
/// Les regles viennent de <c>schema/manifest.schema.json</c> et
/// <c>schema/chart.schema.json</c>, qui font foi, completees par les regles que
/// le schema ne peut pas exprimer : concordance du nombre de notes, concordance
/// des empreintes, unicite des niveaux, derivation de la graine.
/// </para>
/// </remarks>
public static class PackValidator
{
    /// <summary>Verifie un pack sans toucher a l'audio.</summary>
    /// <param name="pack">Le pack lu.</param>
    /// <returns>Tous les constats.</returns>
    public static ValidationResult Validate(LoadedPack pack)
    {
        ArgumentNullException.ThrowIfNull(pack);
        return Validate(pack, probe: null);
    }

    /// <summary>Verifie un pack, en verifiant aussi l'audio si un lecteur est fourni.</summary>
    /// <param name="pack">Le pack lu.</param>
    /// <param name="probe">
    /// Lecteur d'audio. Sans lui, la concordance de <c>audio.sha256</c> n'est
    /// pas verifiee et le validateur le dit, plutot que de laisser croire que
    /// tout a ete controle.
    /// </param>
    /// <returns>Tous les constats.</returns>
    public static ValidationResult Validate(LoadedPack pack, IPackAudioProbe? probe)
    {
        ArgumentNullException.ThrowIfNull(pack);

        ValidationResult result = new();
        CheckArchive(pack, result);
        CheckManifestShape(pack, result);
        CheckManifestFields(pack.Manifest, result);
        CheckCharts(pack, result);
        CheckConventions(pack, result);
        CheckAudio(pack, probe, result);
        return result;
    }

    // ---------------------------------------------------------------- archive

    private static void CheckArchive(LoadedPack pack, ValidationResult result)
    {
        foreach (string entry in pack.Entries)
        {
            if (entry.StartsWith('/')
                || entry.Contains('\\', StringComparison.Ordinal)
                || entry.Split('/').Any(part => part == ".."))
            {
                result.Error(
                    ValidationCodes.PackEntryEscape,
                    $"L'entree « {entry} » sort du pack. {PackFormat.NoPathEscapeRule}",
                    entry);
            }
        }
    }

    // -------------------------------------------------------------- manifeste

    private static void CheckManifestShape(LoadedPack pack, ValidationResult result)
    {
        if (!JsonShape.TryParse(pack.ManifestJson, out JsonElement? root) || root is null)
        {
            result.Error(ValidationCodes.ChartNotJson, "Le manifeste n'est pas du JSON.");
            return;
        }

        JsonShape.CheckKnownProperties(
            root.Value, PackShape.ManifestFields, string.Empty, result, ValidationCodes.UnknownField);
        JsonShape.CheckFieldsOf(
            root.Value, "audio", PackShape.AudioFields, ValidationCodes.UnknownField, result);
        JsonShape.CheckFieldsOf(
            root.Value, "analysis", PackShape.AnalysisFields, ValidationCodes.UnknownField, result);
        JsonShape.CheckFieldsOf(
            root.Value, "generator", PackShape.GeneratorFields, ValidationCodes.UnknownField, result);
        CheckChartEntryShapes(root.Value, result);
    }

    private static void CheckChartEntryShapes(JsonElement root, ValidationResult result)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("charts", out JsonElement charts)
            || charts.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        int index = 0;
        foreach (JsonElement entry in charts.EnumerateArray())
        {
            JsonShape.CheckKnownProperties(
                entry,
                PackShape.ChartEntryFields,
                ValidationResult.JsonPointerAt("/charts", index),
                result,
                ValidationCodes.UnknownField);
            index++;
        }
    }

    private static void CheckManifestFields(Manifest manifest, ValidationResult result)
    {
        if (manifest.Schema is null)
        {
            result.Error(
                ValidationCodes.MissingField,
                "schema est obligatoire.",
                "/schema");
        }
        else if (!string.Equals(manifest.Schema, PackFormat.ManifestSchema, StringComparison.Ordinal))
        {
            result.Error(
                ValidationCodes.SchemaUnknown,
                $"« {manifest.Schema} » n'est pas la version de format que ce lecteur connait. "
                + $"Attendu : « {PackFormat.ManifestSchema} ».",
                "/schema");
        }

        CheckId(manifest, result);
        CheckTitle(manifest, result);
        CheckAudioRef(manifest.Audio, result);
        CheckAnalysis(manifest, result);
        CheckGenerator(manifest.Generator, result);
        CheckChartEntries(manifest, result);
        CheckLicense(manifest.ContentLicense, result);
        CheckMinPlayerVersion(manifest.MinPlayerVersion, result);
    }

    private static void CheckId(Manifest manifest, ValidationResult result)
    {
        if (PackFieldRules.Required(
            result, !string.IsNullOrEmpty(manifest.Id), ValidationCodes.MissingField, "/id", "id"))
        {
            if (!PackFieldRules.IsValidId(manifest.Id))
            {
                result.Error(
                    ValidationCodes.BadValue,
                    $"« {manifest.Id} » n'est pas un identifiant de pack : il commence par une "
                    + "lettre ou un chiffre, puis 2 a 63 caracteres en minuscules, chiffres, tirets "
                    + "ou soulignes.",
                    "/id");
            }
        }
    }

    private static void CheckTitle(Manifest manifest, ValidationResult result)
    {
        if (PackFieldRules.Required(
            result, !string.IsNullOrEmpty(manifest.Title), ValidationCodes.MissingField, "/title", "title"))
        {
            if (manifest.Title!.Length > 200)
            {
                result.Error(
                    ValidationCodes.BadValue,
                    $"title fait {manifest.Title.Length} caracteres ; le maximum est 200.",
                    "/title");
            }
        }

        if (manifest.Artist is { Length: > 200 })
        {
            result.Error(
                ValidationCodes.BadValue,
                $"artist fait {manifest.Artist.Length} caracteres ; le maximum est 200.",
                "/artist");
        }
    }

    private static void CheckAudioRef(AudioRef? audio, ValidationResult result)
    {
        if (!PackFieldRules.Required(
            result, audio is not null, ValidationCodes.MissingField, "/audio", "audio"))
        {
            return;
        }

        AudioRef audioRef = audio!;
        CheckAudioPath(audioRef, result);
        CheckAudioHash(audioRef, result);

        if (PackFieldRules.Required(
            result, audioRef.DurationSeconds is not null, ValidationCodes.MissingField,
            "/audio/duration_seconds", "audio.duration_seconds"))
        {
            double duration = audioRef.DurationSeconds!.Value;
            if (duration <= 0 || duration > 3600)
            {
                result.Error(
                    ValidationCodes.BadValue,
                    $"audio.duration_seconds vaut {PackFieldRules.Num(duration)} ; "
                    + "il doit etre strictement positif et au plus 3600.",
                    "/audio/duration_seconds");
            }
        }

        if (PackFieldRules.Required(
            result, audioRef.PreSkipSeconds is not null, ValidationCodes.MissingField,
            "/audio/pre_skip_seconds", "audio.pre_skip_seconds"))
        {
            if (!PackFieldRules.InRange(audioRef.PreSkipSeconds!.Value, 0, 1))
            {
                result.Error(
                    ValidationCodes.BadValue,
                    $"audio.pre_skip_seconds vaut {PackFieldRules.Num(audioRef.PreSkipSeconds.Value)} ; "
                    + "il doit etre entre 0 et 1.",
                    "/audio/pre_skip_seconds");
            }
        }

        PackFieldRules.Required(
            result, audioRef.GaplessMetadata is not null, ValidationCodes.MissingField,
            "/audio/gapless_metadata", "audio.gapless_metadata");
    }

    /// <summary>Lit une petite entree de pack en memoire. Pour quoi ne pas de flux.</summary>
    private static byte[]? ReadEntryBytes(LoadedPack pack, string entryName)
    {
        using Stream? stream = pack.OpenEntry(entryName);
        if (stream is null)
        {
            return null;
        }

        using MemoryStream buffer = new();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static void CheckAudioPath(AudioRef audio, ValidationResult result)
    {
        if (!PackFieldRules.Required(
            result, !string.IsNullOrEmpty(audio.Path), ValidationCodes.MissingField,
            "/audio/path", "audio.path"))
        {
            return;
        }

        if (!PackFieldRules.IsValidPath(audio.Path))
        {
            result.Error(
                ValidationCodes.BadValue,
                $"« {audio.Path} » n'est pas un chemin relatif de pack : "
                + PackFormat.NoPathEscapeRule
                + " Les segments sont separes par « / » et ne contiennent que lettres, chiffres, "
                + "point, tiret et soulignement.",
                "/audio/path");
        }
    }

    private static void CheckAudioHash(AudioRef audio, ValidationResult result)
    {
        if (!PackFieldRules.Required(
            result, !string.IsNullOrEmpty(audio.Sha256), ValidationCodes.MissingField,
            "/audio/sha256", "audio.sha256"))
        {
            return;
        }

        if (!PackFieldRules.IsValidSha256(audio.Sha256))
        {
            result.Error(
                ValidationCodes.BadValue,
                $"audio.sha256 n'est pas une empreinte SHA-256 : 64 caracteres hexadecimaux "
                + "en minuscules. Attention, l'empreinte porte sur l'audio DECODE, pas sur les "
                + "octets stockes.",
                "/audio/sha256");
        }
    }

    private static void CheckAnalysis(Manifest manifest, ValidationResult result)
    {
        AnalysisInfo? analysis = manifest.Analysis;
        if (!PackFieldRules.Required(
            result, analysis is not null, ValidationCodes.MissingField, "/analysis", "analysis"))
        {
            return;
        }

        if (PackFieldRules.Required(
            result, analysis!.DurationSeconds is not null, ValidationCodes.MissingField,
            "/analysis/duration_seconds", "analysis.duration_seconds"))
        {
            if (analysis.DurationSeconds!.Value <= 0)
            {
                result.Error(
                    ValidationCodes.BadValue,
                    "analysis.duration_seconds doit etre strictement positif.",
                    "/analysis/duration_seconds");
            }
        }

        if (analysis.Bpm is { } bpm && !PackFieldRules.InRange(bpm, 20, 400))
        {
            result.Error(
                ValidationCodes.BadValue,
                $"analysis.bpm vaut {PackFieldRules.Num(bpm)} ; il doit etre entre 20 et 400.",
                "/analysis/bpm");
        }

        CheckConfidence(analysis.BpmConfidence, "/analysis/bpm_confidence", "analysis.bpm_confidence", result);
        CheckConfidence(analysis.KeyConfidence, "/analysis/key_confidence", "analysis.key_confidence", result);
    }

    private static void CheckConfidence(double? value, string jsonPointer, string what, ValidationResult result)
    {
        if (value is { } confidence && !PackFieldRules.InRange(confidence, 0, 1))
        {
            result.Error(
                ValidationCodes.BadValue,
                $"{what} vaut {PackFieldRules.Num(confidence)} ; il doit etre entre 0 et 1.",
                jsonPointer);
        }
    }

    private static void CheckGenerator(GeneratorInfo? generator, ValidationResult result)
    {
        if (!PackFieldRules.Required(
            result, generator is not null, ValidationCodes.MissingField, "/generator", "generator"))
        {
            return;
        }

        if (PackFieldRules.Required(
            result, !string.IsNullOrEmpty(generator!.Name), ValidationCodes.MissingField,
            "/generator/name", "generator.name"))
        {
            if (generator.Name!.Length > 64)
            {
                result.Error(
                    ValidationCodes.BadValue,
                    $"generator.name fait {generator.Name.Length} caracteres ; le maximum est 64.",
                    "/generator/name");
            }
        }

        if (PackFieldRules.Required(
            result, !string.IsNullOrEmpty(generator.Version), ValidationCodes.MissingField,
            "/generator/version", "generator.version"))
        {
            if (!SemanticVersion.IsValid(generator.Version))
            {
                result.Error(
                    ValidationCodes.BadValue,
                    $"« {generator.Version} » n'est pas une version semver (X.Y.Z).",
                    "/generator/version");
            }
        }

        PackFieldRules.Required(
            result, generator.Seed is not null, ValidationCodes.MissingField,
            "/generator/seed", "generator.seed");

        if (generator.Params is not null && generator.Params.Count == 0)
        {
            result.Warning(
                ValidationCodes.BadValue,
                "generator.params est vide ; le generateur declare n'avoir utilise aucun parametre.",
                "/generator/params");
        }
    }

    private static void CheckChartEntries(Manifest manifest, ValidationResult result)
    {
        if (!PackFieldRules.Required(
            result, manifest.Charts is not null, ValidationCodes.MissingField, "/charts", "charts"))
        {
            return;
        }

        IReadOnlyList<ChartEntry> charts = manifest.Charts!;
        if (charts.Count < 1 || charts.Count > 3)
        {
            result.Error(
                ValidationCodes.BadValue,
                $"charts contient {charts.Count} niveau(s) ; le format en accepte de 1 a 3.",
                "/charts");
        }

        HashSet<ChartLevel> seen = new();
        int index = 0;
        foreach (ChartEntry entry in charts)
        {
            string jsonPointer = ValidationResult.JsonPointerAt("/charts", index);
            CheckChartEntry(entry, jsonPointer, result);
            if (entry.Level is { } level && !seen.Add(level))
            {
                result.Error(
                    ValidationCodes.DuplicateLevel,
                    $"Le niveau « {level.FileNameOf()} » apparait deux fois.",
                    ValidationResult.JsonPointerField(jsonPointer, "level"));
            }

            index++;
        }
    }

    private static void CheckChartEntry(ChartEntry entry, string jsonPointer, ValidationResult result)
    {
        PackFieldRules.Required(result, entry.Level is not null, ValidationCodes.MissingField,
            ValidationResult.JsonPointerField(jsonPointer, "level"), "level");

        if (PackFieldRules.Required(
            result, !string.IsNullOrEmpty(entry.File), ValidationCodes.MissingField,
            ValidationResult.JsonPointerField(jsonPointer, "file"), "file")
            && !PackFieldRules.IsValidChartFile(entry.File))
        {
            result.Error(
                ValidationCodes.BadValue,
                $"« {entry.File} » n'est pas un emplacement de chart : "
                + "attendu « charts/xxx.json », en minuscules, tirets et soulignés.",
                ValidationResult.JsonPointerField(jsonPointer, "file"));
        }

        if (PackFieldRules.Required(
            result, entry.NoteCount is not null, ValidationCodes.MissingField,
            ValidationResult.JsonPointerField(jsonPointer, "note_count"), "note_count"))
        {
            int count = entry.NoteCount!.Value;
            if (count < 1 || count > PackFormat.MaxNotes)
            {
                result.Error(
                    ValidationCodes.BadValue,
                    $"note_count vaut {count} ; il doit etre entre 1 et {PackFormat.MaxNotes}.",
                    ValidationResult.JsonPointerField(jsonPointer, "note_count"));
            }
        }

        if (PackFieldRules.Required(
            result, entry.DurationSeconds is not null, ValidationCodes.MissingField,
            ValidationResult.JsonPointerField(jsonPointer, "duration_seconds"), "duration_seconds"))
        {
            double duration = entry.DurationSeconds!.Value;
            if (duration < 0 || duration > PackFormat.MaxNoteTimeSeconds)
            {
                result.Error(
                    ValidationCodes.BadValue,
                    $"duration_seconds vaut {PackFieldRules.Num(duration)} ; "
                    + $"il doit etre entre 0 et {PackFormat.MaxNoteTimeSeconds}.",
                    ValidationResult.JsonPointerField(jsonPointer, "duration_seconds"));
            }
        }

        if (entry.Nps is { } nps && !PackFieldRules.InRange(nps, 0, 30))
        {
            result.Error(
                ValidationCodes.BadValue,
                $"nps vaut {PackFieldRules.Num(nps)} ; il doit etre entre 0 et 30 notes par seconde.",
                ValidationResult.JsonPointerField(jsonPointer, "nps"));
        }

        if (entry.PeakLoad is { } load && !PackFieldRules.InRange(load, 0, 10))
        {
            result.Error(
                ValidationCodes.BadValue,
                $"peak_load vaut {PackFieldRules.Num(load)} ; la charge de jeu va de 0 a 10.",
                ValidationResult.JsonPointerField(jsonPointer, "peak_load"));
        }

        if (entry.Rating is { } rating && (rating < 1 || rating > 20))
        {
            result.Error(
                ValidationCodes.BadValue,
                $"rating vaut {rating} ; c'est un nombre entier entre 1 et 20.",
                ValidationResult.JsonPointerField(jsonPointer, "rating"));
        }
    }

    private static void CheckLicense(string? license, ValidationResult result)
    {
        if (!PackFieldRules.Required(
            result, !string.IsNullOrWhiteSpace(license), ValidationCodes.MissingField,
            "/content_license", "content_license"))
        {
            return;
        }

        LicenseVerdict verdict = SpdxLicenses.Evaluate(license);
        if (verdict.IsRefused)
        {
            result.Error(ValidationCodes.LicenseNotAllowed, verdict.Reason, "/content_license");
        }
        else if (verdict.HasWarning)
        {
            // Un code distinct de celui du refus : un appelant qui filtre sur
            // « licence-not-allowed » pour rejeter un pack ne doit pas voir
            // passer les licences exigeant le partage a l'identique, qui sont
            // acceptees.
            result.Warning(ValidationCodes.LicenseRequiresShareAlike, verdict.Reason, "/content_license");
        }
    }

    private static void CheckMinPlayerVersion(SemanticVersion? version, ValidationResult result)
    {
        if (!PackFieldRules.Required(
            result, version is not null, ValidationCodes.MissingField,
            "/min_player_version", "min_player_version"))
        {
            return;
        }

        if (version! > SemanticVersion.Current)
        {
            result.Warning(
                ValidationCodes.PlayerTooOld,
                $"Ce pack exige un lecteur {version} ; celui-ci est en {SemanticVersion.Current}. "
                + "Le pack sera refuse a l'ouverture.",
                "/min_player_version");
        }
    }

    // ----------------------------------------------------------------- charts

    private static void CheckCharts(LoadedPack pack, ValidationResult result)
    {
        if (pack.Manifest.Charts is null)
        {
            return;
        }

        for (int index = 0; index < pack.Manifest.Charts.Count; index++)
        {
            CheckChart(
                pack,
                index,
                ValidationResult.JsonPointerAt("/charts", index),
                result);
        }
    }

    private static void CheckChart(LoadedPack pack, int index, string entryPointer, ValidationResult result)
    {
        LoadedChart loaded = pack.Charts[index];
        if (loaded.ReadError is not null)
        {
            if (loaded.FileExists)
            {
                result.Error(
                    ValidationCodes.ChartNotJson,
                    $"« {loaded.File} » n'a pas pu etre lu : {loaded.ReadError}",
                    ValidationResult.JsonPointerField(entryPointer, "file"));
            }
            else
            {
                result.Error(
                    ValidationCodes.ChartMissingFromPack,
                    string.IsNullOrEmpty(loaded.File)
                        ? "Le manifeste n'indique pas de fichier pour ce niveau."
                        : $"« {loaded.File} » est annonce mais absent du pack.",
                    ValidationResult.JsonPointerField(entryPointer, "file"));
            }

            return;
        }

        CheckChartShape(loaded, result);
        CheckChartFields(loaded, pack, result);
    }

    private static void CheckChartShape(LoadedChart loaded, ValidationResult result)
    {
        if (loaded.Json is null || !JsonShape.TryParse(loaded.Json, out JsonElement? root) || root is null)
        {
            return;
        }

        JsonShape.CheckKnownProperties(
            root.Value, PackShape.ChartFields, string.Empty, result, ValidationCodes.ChartUnknownField);
        JsonShape.CheckFieldsOf(
            root.Value, "generator", PackShape.ChartGeneratorFields,
            ValidationCodes.ChartUnknownField, result);

        if (root.Value.ValueKind == JsonValueKind.Object
            && root.Value.TryGetProperty("notes", out JsonElement notes)
            && notes.ValueKind == JsonValueKind.Array)
        {
            int index = 0;
            foreach (JsonElement note in notes.EnumerateArray())
            {
                JsonShape.CheckKnownProperties(
                    note, PackShape.NoteFields, "/notes/" + index,
                    result, ValidationCodes.ChartUnknownField);
                index++;
            }
        }
    }

    private static void CheckChartFields(LoadedChart loaded, LoadedPack pack, ValidationResult result)
    {
        Chart chart = loaded.Chart!;
        ChartEntry entry = loaded.Entry;

        if (chart.Schema is null)
        {
            result.Error(ValidationCodes.ChartMissingField, "schema est obligatoire.", "/schema");
        }
        else if (!string.Equals(chart.Schema, PackFormat.ChartSchema, StringComparison.Ordinal))
        {
            result.Error(
                ValidationCodes.ChartSchemaUnknown,
                $"« {chart.Schema} » n'est pas la version de format que ce lecteur connait. "
                + $"Attendu : « {PackFormat.ChartSchema} ».",
                "/schema");
        }

        if (chart.Level is null)
        {
            result.Error(ValidationCodes.ChartMissingField, "level est obligatoire.", "/level");
        }
        else if (entry.Level is { } declared && chart.Level != declared)
        {
            result.Error(
                ValidationCodes.ChartLevelMismatch,
                $"Le manifeste annonce « {declared.FileNameOf()} » pour ce fichier, "
                + $"qui se dit « {chart.Level.Value.FileNameOf()} ».",
                "/level");
        }

        CheckChartAudioHash(chart, pack, result);

        if (chart.OffsetSeconds is { } offset
            && !PackFieldRules.InRange(offset, 0, 60))
        {
            result.Error(
                ValidationCodes.ChartMissingField,
                $"offset_seconds vaut {PackFieldRules.Num(offset)} ; il doit etre entre 0 et 60.",
                "/offset_seconds");
        }

        CheckChartGenerator(chart.Generator, pack.Manifest, chart.Level, result);
        CheckNotes(chart, entry, result);
    }

    private static void CheckChartAudioHash(Chart chart, LoadedPack pack, ValidationResult result)
    {
        if (!PackFieldRules.Required(
            result, !string.IsNullOrEmpty(chart.AudioSha256), ValidationCodes.ChartMissingField,
            "/audio_sha256", "audio_sha256"))
        {
            return;
        }

        if (!PackFieldRules.IsValidSha256(chart.AudioSha256))
        {
            result.Error(
                ValidationCodes.BadValue,
                "audio_sha256 n'est pas une empreinte SHA-256 : 64 caracteres hexadecimaux en "
                + "minuscules.",
                "/audio_sha256");
            return;
        }

        if (!string.Equals(chart.AudioSha256, pack.Manifest.Audio?.Sha256, StringComparison.Ordinal))
        {
            result.Error(
                ValidationCodes.ChartAudioMismatch,
                "La chart vise un audio different de celui du manifeste : les deux doivent porter "
                + "la meme empreinte de l'audio decode.",
                "/audio_sha256");
        }
    }

    private static void CheckChartGenerator(
        GeneratorRef? generator,
        Manifest manifest,
        ChartLevel? level,
        ValidationResult result)
    {
        if (!PackFieldRules.Required(
            result, generator is not null, ValidationCodes.ChartMissingField,
            "/generator", "generator"))
        {
            return;
        }

        PackFieldRules.Required(
            result, !string.IsNullOrEmpty(generator!.Name), ValidationCodes.ChartMissingField,
            "/generator/name", "generator.name");
        PackFieldRules.Required(
            result, !string.IsNullOrEmpty(generator.Version), ValidationCodes.ChartMissingField,
            "/generator/version", "generator.version");

        if (PackFieldRules.Required(
            result, generator.Seed is not null, ValidationCodes.ChartMissingField,
            "/generator/seed", "generator.seed"))
        {
            CheckChartSeed(generator.Seed!.Value, "/generator/seed", manifest, level, result);
        }
    }

    /// <summary>
    /// Verifie qu'une graine de chart derive bien de l'audio, de la version du
    /// generateur et du niveau.
    /// </summary>
    /// <remarks>
    /// <para>
    /// C'est la seule regle du format qui garantit qu'un pack n'a pas ete
    /// retouche apres coup. Tout le reste se verifie en comparant des
    /// declarations entre elles ; la graine, elle, se recalcule. Un chart dont
    /// les notes ont ete reordonnees a la main garde une graine qui ne correspond
    /// plus, et le validateur le voit.
    /// </para>
    /// <para>
    /// Si le trio est incomplet, on ne peut rien recalculer. Les champs
    /// manquants sont deja signales ailleurs, et une erreur en double n'aide
    /// personne.
    /// </para>
    /// </remarks>
    private static void CheckChartSeed(
        ulong seed,
        string jsonPointer,
        Manifest manifest,
        ChartLevel? level,
        ValidationResult result)
    {
        string? audio = manifest.Audio?.Sha256;
        string? version = manifest.Generator?.Version;
        if (audio is null || version is null || level is null)
        {
            return;
        }

        string levelName = level.Value.FileNameOf();
        string? material = SeedMaterialOf(manifest, audio);
        ulong expected = SeedDerivation.ChartSeedValue(material, version, levelName);
        if (seed != expected)
        {
            result.Error(
                ValidationCodes.SeedNotDerived,
                $"generator.seed vaut {Invariant(seed)}, alors que la derivee du materiau de graine "
                + $"(« {material} »), de la version « {version} » et du niveau « {levelName} » "
                + $"donne {Invariant(expected)}. La graine depend du contenu decode de l'audio, "
                + "jamais d'un nom de fichier : c'est ce qui la rend reproductible.",
                jsonPointer);
        }
    }

    /// <summary>
    /// Le materiau dont les graines sont derivees : celui que le generateur a
    /// note, ou l'empreinte de l'audio quand il n'en a note aucun.
    /// </summary>
    /// <remarks>
    /// Par defaut le materiau est l'empreinte, et il n'est pas reecrit dans
    /// <c>generator.params</c>. Un generateur qui impose sa propre graine doit
    /// la noter, sinon personne ne pourrait recalculer <c>generator.seed</c> et
    /// la regle de derivaison deviendrait unverifiable — donc inerte.
    /// </remarks>
    private static string SeedMaterialOf(Manifest manifest, string audioSha256)
    {
        if (manifest.Generator?.Params?["seed_material"] is JsonValue value
            && value.TryGetValue<string>(out string? material)
            && PackFieldRules.IsValidSha256(material))
        {
            return material;
        }

        return audioSha256;
    }

    private static string Invariant(ulong value) =>
        value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static void CheckNotes(Chart chart, ChartEntry entry, ValidationResult result)
    {
        if (!PackFieldRules.Required(
            result, chart.Notes is not null, ValidationCodes.ChartMissingField,
            "/notes", "notes"))
        {
            return;
        }

        IReadOnlyList<Note> notes = chart.Notes!;
        if (notes.Count < 1)
        {
            result.Error(
                ValidationCodes.ChartMissingField,
                "notes est vide : un niveau sans note n'est pas un niveau.",
                "/notes");
        }

        if (notes.Count > PackFormat.MaxNotes)
        {
            result.Error(
                ValidationCodes.ChartMissingField,
                $"notes contient {notes.Count} notes ; le maximum est {PackFormat.MaxNotes}.",
                "/notes");
        }

        if (entry.NoteCount is { } declared && declared != notes.Count)
        {
            result.Error(
                ValidationCodes.NoteCountMismatch,
                $"Le manifeste annonce {declared} note(s) ; le fichier en contient {notes.Count}. "
                + "C'est le manifeste qui a raison : un lecteur qui affiche « 1 240 notes » doit "
                + "pouvoir les compter.",
                "/notes");
        }

        double previous = double.NegativeInfinity;
        for (int index = 0; index < notes.Count; index++)
        {
            Note note = notes[index];
            string jsonPointer = ValidationResult.JsonPointerAt("/notes", index);

            if (note.Time < 0 || note.Time > PackFormat.MaxNoteTimeSeconds)
            {
                result.Error(
                    ValidationCodes.NoteOutOfRange,
                    $"« t » vaut {PackFieldRules.Num(note.Time)} ; il doit etre entre 0 et "
                    + $"{PackFormat.MaxNoteTimeSeconds} secondes.",
                    ValidationResult.JsonPointerField(jsonPointer, "t"));
            }
            else if (note.Time < previous)
            {
                result.Error(
                    ValidationCodes.NotesUnsorted,
                    $"« t » vaut {PackFieldRules.Num(note.Time)}, moins que le "
                    + $"{PackFieldRules.Num(previous)} de la note precedente. Les notes se lisent "
                    + "dans l'ordre, et doivent donc etre triees.",
                    ValidationResult.JsonPointerField(jsonPointer, "t"));
            }
            else
            {
                previous = note.Time;
            }

            if (!note.Key.HasValue)
            {
                result.Error(
                    ValidationCodes.NoteBadKey,
                    "« k » doit designer une touche de la grille 3x3 (0 a 8) ou un volant "
                    + "(WHEEL_L, WHEEL_R).",
                    ValidationResult.JsonPointerField(jsonPointer, "k"));
            }

            if (note.Duration is { } duration && !Note.IsValidHoldDuration(duration))
            {
                result.Error(
                    ValidationCodes.NoteOutOfRange,
                    $"« d » vaut {PackFieldRules.Num(duration)} ; une tenue dure de plus de 0 "
                    + $"a {PackFormat.MaxHoldSeconds} secondes.",
                    ValidationResult.JsonPointerField(jsonPointer, "d"));
            }

            if (note.Force is { } force && (force < 0 || force > 8))
            {
                result.Error(
                    ValidationCodes.NoteOutOfRange,
                    $"« v » vaut {force} ; la force d'un coup va de 0 a 8.",
                    ValidationResult.JsonPointerField(jsonPointer, "v"));
            }

            if (note.WindowScale is { } window
                && !PackFieldRules.InRange(window, 0.5, 3))
            {
                result.Error(
                    ValidationCodes.NoteOutOfRange,
                    $"« w » vaut {PackFieldRules.Num(window)} ; le multiplicateur de fenetre va "
                    + "de 0,5 a 3. Il agit sur la tolerance du jugement, pas sur le son.",
                    ValidationResult.JsonPointerField(jsonPointer, "w"));
            }

            if (note.PhaseShift is { } phase && !PackFieldRules.IsValidPhaseShift(phase))
            {
                result.Error(
                    ValidationCodes.NoteBadPhase,
                    $"« s » vaut {PackFieldRules.Num(phase)} ; le decalage de phase est un "
                    + "multiple d'un huitieme de temps : 0, 0,125, 0,25 ou 0,375.",
                    ValidationResult.JsonPointerField(jsonPointer, "s"));
            }
        }

        if (entry.DurationSeconds is { } declaredDuration && chart.LastNoteTime > declaredDuration)
        {
            result.Error(
                ValidationCodes.ChartTooShort,
                $"La derniere note est a {PackFieldRules.Num(chart.LastNoteTime)} s, alors que le "
                + $"manifeste annonce un niveau de {PackFieldRules.Num(declaredDuration)} s. "
                + "Le niveau serait coupe avant sa fin.",
                "/notes");
        }
    }

    // ----------------------------------------------------------- conventions

    private static void CheckConventions(LoadedPack pack, ValidationResult result)
    {
        if (!pack.HasEntry(PackFormat.ReportFileName))
        {
            result.Warning(
                ValidationCodes.ReportMissing,
                $"« {PackFormat.ReportFileName} » est absent. Le manifeste ne peut pas le "
                + "declarer — il n'accepte aucun champ inconnu — donc c'est une convention de "
                + "nom, comme « LICENSE.txt ».");
        }

        if (!pack.HasEntry(PackFormat.LicenseFileName))
        {
            result.Warning(
                ValidationCodes.LicenseFileMissing,
                $"« {PackFormat.LicenseFileName} » est absent. Un lecteur doit pouvoir afficher "
                + "le texte de la licence sans aller le chercher sur le Web.");
        }
        else if (pack.Manifest.ContentLicense is { } declared)
        {
            byte[]? text = ReadEntryBytes(pack, PackFormat.LicenseFileName);
            string content = text is null ? string.Empty : System.Text.Encoding.UTF8.GetString(text);
            if (!content.Contains(declared, StringComparison.Ordinal))
            {
                result.Warning(
                    ValidationCodes.LicenseFileMismatch,
                    $"« {PackFormat.LicenseFileName} » ne contient pas l'identifiant "
                    + $"« {declared} » annonce dans le manifeste.",
                    "/content_license");
            }
        }
    }

    // ------------------------------------------------------------------ audio

    private static void CheckAudio(LoadedPack pack, IPackAudioProbe? probe, ValidationResult result)
    {
        AudioRef? audio = pack.Manifest.Audio;
        if (audio is null || string.IsNullOrEmpty(audio.Path))
        {
            return;
        }

        if (!pack.HasEntry(audio.Path))
        {
            result.Error(
                ValidationCodes.AudioMissing,
                $"« {audio.Path} » est annonce mais absent du pack. "
                + "Le chemin est relatif a la racine du pack, jamais absolu.",
                "/audio/path");
            return;
        }

        if (probe is null)
        {
            result.Warning(
                ValidationCodes.AudioUnreadable,
                "L'audio n'a pas ete verifie : aucun lecteur n'a ete fourni a la validation. "
                + "Les regles qui portent sur l'audio ne sont pas controlees.",
                "/audio/sha256");
            return;
        }

        using Stream? stream = pack.OpenEntry(audio.Path);
        if (stream is null)
        {
            return;
        }

        if (!probe.TryInspect(stream, out PackAudioFacts facts))
        {
            result.Error(
                ValidationCodes.AudioUnreadable,
                $"« {audio.Path} » n'a pas pu etre decode. Le format ne definit que le WAV PCM "
                + "et IEEE float ; tout le reste doit etre converti avant d'enter dans un pack.",
                "/audio/path");
            return;
        }

        if (!string.IsNullOrEmpty(audio.Sha256)
            && !string.Equals(audio.Sha256, facts.Sha256Hex, StringComparison.Ordinal))
        {
            result.Error(
                ValidationCodes.AudioHashMismatch,
                $"L'empreinte de l'audio decode est {facts.Sha256Hex}, mais le manifeste annonce "
                + $"{audio.Sha256}. L'empreinte porte sur l'audio DECODE : le meme morceau "
                + "encode en WAV 16 et en FLAC n'a donc pas la meme empreinte.",
                "/audio/sha256");
        }

        if (audio.DurationSeconds is { } declared
            && Math.Abs(declared - facts.DurationSeconds) > PackFormat.DurationToleranceSeconds)
        {
            result.Error(
                ValidationCodes.DurationMismatch,
                $"audio.duration_seconds annonce {PackFieldRules.Num(declared)} s, l'audio en fait "
                + $"{PackFieldRules.Num(facts.DurationSeconds)} s. Le schema tolere 5 ms, pas plus.",
                "/audio/duration_seconds");
        }

        if (pack.Manifest.Analysis?.DurationSeconds is { } analysed
            && audio.DurationSeconds is { } real
            && Math.Abs(analysed - real) > PackFormat.DurationToleranceSeconds)
        {
            result.Error(
                ValidationCodes.DurationMismatch,
                $"analysis.duration_seconds vaut {PackFieldRules.Num(analysed)} s et "
                + $"audio.duration_seconds {PackFieldRules.Num(real)} s : ils doivent decrire le "
                + "meme fichier.",
                "/analysis/duration_seconds");
        }
    }
}
