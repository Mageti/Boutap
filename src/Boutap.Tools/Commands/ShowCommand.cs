// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Globalization;
using System.Text.Json.Nodes;
using Boutap.Core.Pack;

namespace Boutap.Tools.Commands;

/// <summary>Montre ce qu'il y a dans un pack, sans le valider.</summary>
/// <remarks>
/// <para>
/// <c>validate</c> repond a « est-ce que ce pack est bon ? ». <c>show</c>
/// repond a « qu'est-ce que ce pack contient ? », ce qu'on veut savoir avant
/// d'installer un morceau inconnu. Il ne rend aucun code de sortie d'erreur
/// pour un pack invalide : afficher un pack casse est un succes.
/// </para>
/// </remarks>
public sealed class ShowCommand : ICommand
{
    /// <inheritdoc/>
    public string Name => "show";

    /// <inheritdoc/>
    public string Summary => "Montre le contenu d'un pack : titre, licence, audio, charts.";

    /// <inheritdoc/>
    public string Usage => "boutap show <pack.btp> [--level <niveau>] [--json]";

    /// <inheritdoc/>
    public IReadOnlyList<string> Flags => ["json"];

    public int Run(CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        IReadOnlyList<string> paths = context.Rest();
        if (paths.Count == 0)
        {
            context.Warn("Usage : boutap show <pack.btp> [--level <niveau>] [--json]");
            return ExitCodes.UsageError;
        }

        ChartLevel? only = null;
        if (context.TryGetValue("level", out string? levelName))
        {
            if (!ChartLevels.TryParse(levelName, out ChartLevel parsed))
            {
                context.Warn(
                    $"--level attend l'un de {string.Join(", ", ChartLevels.All.Select(l => l.FileNameOf()))}, pas « {levelName} ».");
                return ExitCodes.UsageError;
            }

            only = parsed;
        }

        int failures = 0;

        foreach (string path in paths)
        {
            if (!File.Exists(path))
            {
                context.Warn($"« {path} » n'existe pas.");
                failures++;
                continue;
            }

            LoadedPack pack;
            try
            {
                pack = PackReader.Read(path);
            }
            catch (Exception ex) when (ex is PackFormatException or IOException or UnauthorizedAccessException)
            {
                context.Warn($"{path} : {ex.Message}");
                failures++;
                continue;
            }

            using (pack)
            {
                if (context.Has("json"))
                {
                    context.Out.WriteLine(ToJson(pack, only).ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                }
                else
                {
                    Text(context, pack, only);
                }
            }
        }

        return failures == 0 ? ExitCodes.Success : ExitCodes.Failure;
    }

    private static void Text(CommandContext context, LoadedPack pack, ChartLevel? only)
    {
        Manifest manifest = pack.Manifest;
        AudioRef? audio = manifest.Audio;
        AnalysisInfo? analysis = manifest.Analysis;
        GeneratorInfo? generator = manifest.Generator;

        context.Out.WriteLine(pack.Path);
        context.Out.WriteLine($"  id              {Or(manifest.Id)}");
        context.Out.WriteLine($"  titre           {Or(manifest.Title)}");
        context.Out.WriteLine($"  artiste         {Or(manifest.Artist)}");
        context.Out.WriteLine($"  licence         {Or(manifest.ContentLicense)}");
        context.Out.WriteLine($"  version joueur  {Or(manifest.MinPlayerVersion?.ToString())}");
        context.Out.WriteLine();

        if (audio is not null)
        {
            context.Out.WriteLine("  audio");
            context.Out.WriteLine($"    chemin        {Or(audio.Path)}");
            context.Out.WriteLine($"    sha256        {Or(audio.Sha256)}");
            context.Out.WriteLine($"    duree         {Seconds(audio.DurationSeconds)}");
            context.Out.WriteLine($"    pre-skip      {Seconds(audio.PreSkipSeconds)}");
            context.Out.WriteLine($"    gapless       {Or(audio.GaplessMetadata?.ToString())}");
            context.Out.WriteLine();
        }

        if (analysis is not null)
        {
            context.Out.WriteLine("  analyse");
            context.Out.WriteLine($"    bpm           {Number(analysis.Bpm)}");
            context.Out.WriteLine($"    confiance bpm {Number(analysis.BpmConfidence)}");
            context.Out.WriteLine($"    tonique       {Or(analysis.Key?.ToString())}");
            context.Out.WriteLine($"    confiance ton {Number(analysis.KeyConfidence)}");
            context.Out.WriteLine();
        }

        if (generator is not null)
        {
            context.Out.WriteLine("  generateur");
            context.Out.WriteLine($"    nom           {Or(generator.Name)}");
            context.Out.WriteLine($"    version       {Or(generator.Version)}");
            context.Out.WriteLine($"    graine        {generator.Seed?.ToString(CultureInfo.InvariantCulture) ?? "-"}");
            context.Out.WriteLine();
        }

        context.Out.WriteLine("  charts");
        foreach (LoadedChart chart in pack.Charts)
        {
            ChartLevel? level = chart.Entry.Level;
            if (only is not null && level != only)
            {
                continue;
            }

            context.Out.WriteLine(
                $"    {(level is null ? "?" : level.Value.FileNameOf()),-8} " +
                $"{chart.Entry.NoteCount ?? 0,6} notes  " +
                $"{Seconds(chart.Entry.DurationSeconds),10}  " +
                $"nps {Number(chart.Entry.Nps)}  " +
                $"charge {Number(chart.Entry.PeakLoad)}/10  " +
                $"note {chart.Entry.Rating?.ToString(CultureInfo.InvariantCulture) ?? "-"}/20");

            if (!chart.IsReadable && chart.ReadError is not null)
            {
                context.Out.WriteLine($"             illisible : {chart.ReadError}");
            }
        }
    }

    private static JsonObject ToJson(LoadedPack pack, ChartLevel? only)
    {
        ArgumentNullException.ThrowIfNull(pack);

        Manifest manifest = pack.Manifest;
        AudioRef? audio = manifest.Audio;
        AnalysisInfo? analysis = manifest.Analysis;
        GeneratorInfo? generator = manifest.Generator;

        JsonArray charts = new();
        foreach (LoadedChart chart in pack.Charts)
        {
            if (only is not null && chart.Entry.Level != only)
            {
                continue;
            }

            charts.Add(new JsonObject
            {
                ["level"] = chart.Entry.Level?.FileNameOf(),
                ["file"] = chart.Entry.File,
                ["note_count"] = chart.Entry.NoteCount,
                ["duration_seconds"] = chart.Entry.DurationSeconds,
                ["nps"] = chart.Entry.Nps,
                ["peak_load"] = chart.Entry.PeakLoad,
                ["rating"] = chart.Entry.Rating,
                ["readable"] = chart.IsReadable,
            });
        }

        return new JsonObject
        {
            ["path"] = pack.Path,
            ["id"] = manifest.Id,
            ["title"] = manifest.Title,
            ["artist"] = manifest.Artist,
            ["content_license"] = manifest.ContentLicense,
            ["min_player_version"] = manifest.MinPlayerVersion?.ToString(),
            ["audio"] = audio is null
                ? null
                : new JsonObject
                {
                    ["path"] = audio.Path,
                    ["sha256"] = audio.Sha256,
                    ["duration_seconds"] = audio.DurationSeconds,
                    ["pre_skip_seconds"] = audio.PreSkipSeconds,
                    ["gapless_metadata"] = audio.GaplessMetadata?.ToString(),
                },
            ["analysis"] = analysis is null
                ? null
                : new JsonObject
                {
                    ["bpm"] = analysis.Bpm,
                    ["bpm_confidence"] = analysis.BpmConfidence,
                    ["key"] = analysis.Key?.ToString(),
                    ["key_confidence"] = analysis.KeyConfidence,
                },
            ["generator"] = generator is null
                ? null
                : new JsonObject
                {
                    ["name"] = generator.Name,
                    ["version"] = generator.Version,
                    ["seed"] = generator.Seed,
                },
            ["charts"] = charts,
        };
    }

    private static string Or(string? value) => string.IsNullOrEmpty(value) ? "-" : value;

    private static string Number(double? value) => value?.ToString("0.###", CultureInfo.InvariantCulture) ?? "-";

    private static string Seconds(double? value) =>
        value is null ? "-" : value.Value.ToString("0.###", CultureInfo.InvariantCulture) + " s";
}
