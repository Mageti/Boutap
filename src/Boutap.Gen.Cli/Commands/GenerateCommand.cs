// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
//
// « boutap-gen generate » : ecrit un pack .btp.

using System.Globalization;
using System.Text.Json.Nodes;
using Boutap.Audio;
using Boutap.Core.Common;
using Boutap.Core.Pack;
using Boutap.Gen.Analysis;
using Boutap.Gen.Compose;

namespace Boutap.Gen.Cli;

/// <summary>Transforme un WAV en pack.</summary>
internal static class GenerateCommand
{
    private const string DefaultAudioPath = "audio.wav";
    private const string DefaultLicense = "CC0-1.0";

    /// <summary>Execute la commande.</summary>
    /// <param name="args">Les arguments de la ligne de commande.</param>
    /// <param name="output">Flux de sortie.</param>
    /// <param name="diagnostics">Flux d'erreur.</param>
    public static int Run(IReadOnlyList<string> args, TextWriter output, TextWriter diagnostics)
    {
        // -o et --output n'appartiennent qu'a cette commande : ReadCommon doit
        // les tolérer sans les manger, sinon il les prendrait pour des
        // positionnels et les perdrait.
        GenProgram.CommonOptions options = GenProgram.ReadCommon(args, 1, "-o", "--output");
        if (options.Positionals.Count == 0)
        {
            diagnostics.WriteLine("Usage : boutap-gen generate <audio> -o <pack.btp> [options]");
            return GenProgram.UsageError;
        }

        string? outputPath = ReadOption(args, "-o") ?? ReadOption(args, "--output");
        if (!options.DryRun && string.IsNullOrWhiteSpace(outputPath))
        {
            diagnostics.WriteLine("Il faut ou -o <pack.btp>, ou --dry-run pour ne rien ecrire.");
            return GenProgram.UsageError;
        }

        LoadedAudio audio = AudioLoader.Load(options.Positionals[0]);
        GenerationResult result = Pipeline.Run(
            audio.Samples,
            audio.SampleRate,
            audio.DurationSeconds,
            audio.CanonicalSha256Hex,
            SemanticVersion.Current.ToString(),
            GenProgram.ProfilesFor(options.Levels),
            options.SeedHex);

        var identity = new PackIdentity(
            Identifier.FromPath(options.Positionals[0]),
            TitleFromPath(options.Positionals[0]),
            string.Empty,
            DefaultLicense,
            DefaultAudioPath);

        PackContent pack = Pipeline.BuildPack(
            result, identity, audio.CanonicalBytes, audio.Gapless, SemanticVersion.Current);

        if (!options.DryRun)
        {
            PackWriter.Write(outputPath!, pack);
        }

        if (options.Json)
        {
            output.WriteLine(DescribeJson(result, pack, outputPath, options.DryRun).ToJsonString(GenJson.Compact));
            return GenProgram.Success;
        }

        Report(result, pack, outputPath, options.DryRun, output);
        return GenProgram.Success;
    }

    private static void Report(GenerationResult result, PackContent pack, string? outputPath, bool dryRun, TextWriter output)
    {
        output.WriteLine(dryRun
            ? "Simulation terminee, rien n'a ete ecrit."
            : $"Pack ecrit : {outputPath}");
        output.WriteLine($"Graine du pack : {pack.Manifest.Generator?.Seed}");
        output.WriteLine(string.Empty);
        output.WriteLine($"{"Niveau",-10} {"Notes",6} {"Charge/10",10} {"Note/20",8}  Fichier");
        foreach (Chart chart in pack.Charts)
        {
            output.WriteLine(
                $"{GenJson.Level(chart.Level ?? ChartLevel.Berceau),-10} {chart.NoteCount,6} " +
                $"{GenJson.Fixed(PeakLoadOf(pack, chart), 2),10} {RatingOf(pack, chart),8}  " +
                $"{PackFormat.ChartFileName(chart.Level ?? ChartLevel.Berceau)}");
        }

        output.WriteLine(string.Empty);
        output.WriteLine("Verifiez le resultat avec : boutap validate " + (outputPath ?? "<pack.btp>"));
    }

    private static JsonObject DescribeJson(GenerationResult result, PackContent pack, string? outputPath, bool dryRun)
    {
        var charts = new JsonArray();
        foreach (Chart chart in pack.Charts)
        {
            charts.Add(new JsonObject
            {
                ["level"] = GenJson.Level(chart.Level ?? ChartLevel.Berceau),
                ["file"] = PackFormat.ChartFileName(chart.Level ?? ChartLevel.Berceau),
                ["note_count"] = chart.NoteCount,
                ["peak_load"] = GenJson.Round(PeakLoadOf(pack, chart), 3),
                ["rating"] = RatingOf(pack, chart),
            });
        }

        return new JsonObject
        {
            ["schema"] = "boutap/gen-report/1",
            ["written"] = !dryRun,
            ["path"] = outputPath,
            ["seed"] = pack.Manifest.Generator?.Seed,
            ["seed_material"] = result.Options.SeedMaterialHex,
            ["tempo_bpm"] = GenJson.Round(result.Track.Beats.TempoBpm),
            ["key"] = result.Track.Key is null ? null : result.Track.Key.Value.Key.ToString(),
            ["charts"] = charts,
        };
    }

    private static double PeakLoadOf(PackContent pack, Chart chart)
    {
        return FindEntry(pack, chart).PeakLoad ?? 0;
    }

    private static int RatingOf(PackContent pack, Chart chart)
    {
        return FindEntry(pack, chart).Rating ?? 0;
    }

    private static ChartEntry FindEntry(PackContent pack, Chart chart)
    {
        if (pack.Manifest.Charts is not null)
        {
            foreach (ChartEntry entry in pack.Manifest.Charts)
            {
                if (entry.Level == chart.Level)
                {
                    return entry;
                }
            }
        }

        throw new PackFormatException($"Le pack ne declare aucune entree pour le niveau {chart.Level}.");
    }

    private static string? ReadOption(IReadOnlyList<string> args, string name)
    {
        for (int i = 0; i < args.Count - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.Ordinal))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static string TitleFromPath(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        return string.IsNullOrWhiteSpace(name) ? "Sans titre" : name;
    }
}
