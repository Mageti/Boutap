// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
//
// « boutap-gen analyse » : que contient ce morceau ?

using System.Text.Json;
using System.Text.Json.Nodes;
using Boutap.Audio;
using Boutap.Core.Common;
using Boutap.Core.Pack;
using Boutap.Gen.Analysis;
using Boutap.Gen.Compose;

namespace Boutap.Gen.Cli;

/// <summary>Decrit un morceau sans rien ecrire.</summary>
internal static class AnalyseCommand
{
    /// <summary>Execute la commande.</summary>
    /// <param name="args">Les arguments de la ligne de commande.</param>
    /// <param name="output">Flux de sortie.</param>
    /// <param name="diagnostics">Flux d'erreur.</param>
    public static int Run(IReadOnlyList<string> args, TextWriter output, TextWriter diagnostics)
    {
        GenProgram.CommonOptions options = GenProgram.ReadCommon(args, 1);
        if (options.Positionals.Count == 0)
        {
            diagnostics.WriteLine("Usage : boutap-gen analyse <audio>");
            return GenProgram.UsageError;
        }

        LoadedAudio audio = AudioLoader.Load(options.Positionals[0]);
        GenerationResult result = Pipeline.Run(
            audio.Samples,
            audio.SampleRate,
            audio.DurationSeconds,
            audio.CanonicalSha256Hex,
            SemanticVersion.Current.ToString(),
            GenProgram.ProfilesFor(options.Levels));

        if (options.Json)
        {
            output.WriteLine(DescribeJson(result, audio).ToJsonString(GenJson.Compact));
            return GenProgram.Success;
        }

        Describe(result, audio, output);
        return GenProgram.Success;
    }

    private static void Describe(GenerationResult result, LoadedAudio audio, TextWriter output)
    {
        TrackProfile track = result.Track;
        BeatTrack beats = track.Beats;

        output.WriteLine($"Audio     : {audio.SampleRate} Hz, {GenJson.Fixed(audio.DurationSeconds, 3)} s");
        output.WriteLine($"Empreinte : {audio.CanonicalSha256Hex}");
        output.WriteLine($"Gapless   : {WavGapless.Describe(audio.Gapless)}");
        output.WriteLine(string.Empty);
        output.WriteLine($"Tempo     : {beats.TempoBpm:0.00} BPM ({beats.Confidence:P0} de confiance, {beats.BeatsSeconds.Count} battements)");

        if (!beats.WithinTolerance)
        {
            output.WriteLine($"            l'ecart a l'apriori depasse 10 % : a verifier a l'oreille.");
        }

        output.WriteLine(track.Key is null
            ? "Tonalite  : aucune ne ressort (correlation trop faible)"
            : $"Tonalite  : {track.Key.Value.Key} ({track.Key.Value.Confidence:P0} de confiance)");

        output.WriteLine(string.Empty);
        output.WriteLine($"{"Niveau",-10} {"Notes",6} {"Charge/10",10} {"Note/20",8} {"Retirees",9}  Motifs");
        foreach (ChartDraft draft in result.Drafts)
        {
            IReadOnlyList<string> reasons = Summarize(draft.Removed);
            output.WriteLine(
                $"{GenJson.Level(draft.Level),-10} {draft.Notes.Count,6} " +
                $"{GenJson.Fixed(Pipeline.PeakLoad(draft.Notes), 2),10} " +
                $"{Pipeline.DifficultyRating(draft),8} {draft.Removed.Count,9}  " +
                $"{string.Join(", ", reasons)}");
        }

        output.WriteLine(string.Empty);
        output.WriteLine("La generation est deterministe : meme audio, meme graine, memes octets.");
    }

    private static List<string> Summarize(IReadOnlyList<ReadabilityFinding> findings)
    {
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (ReadabilityFinding finding in findings)
        {
            counts[finding.Rule] = counts.TryGetValue(finding.Rule, out int n) ? n + 1 : 1;
        }

        var reasons = new List<string>(counts.Count);
        foreach (KeyValuePair<string, int> pair in counts)
        {
            reasons.Add($"{pair.Key} x{pair.Value}");
        }

        return reasons;
    }

    private static JsonObject DescribeJson(GenerationResult result, LoadedAudio audio)
    {
        TrackProfile track = result.Track;
        BeatTrack beats = track.Beats;

        var levels = new JsonArray();
        foreach (ChartDraft draft in result.Drafts)
        {
            levels.Add(new JsonObject
            {
                ["level"] = GenJson.Level(draft.Level),
                ["notes"] = draft.Notes.Count,
                ["peak_load"] = GenJson.Round(Pipeline.PeakLoad(draft.Notes), 3),
                ["rating"] = Pipeline.DifficultyRating(draft),
                ["removed"] = draft.Removed.Count,
                ["unreachable"] = draft.Unreachable.Count,
            });
        }

        return new JsonObject
        {
            ["schema"] = "boutap/gen-analysis/1",
            ["sample_rate"] = audio.SampleRate,
            ["duration_seconds"] = audio.DurationSeconds,
            ["audio_sha256"] = audio.CanonicalSha256Hex,
            ["gapless"] = GenJson.Gapless(audio.Gapless),
            ["tempo_bpm"] = GenJson.Round(beats.TempoBpm),
            ["tempo_confidence"] = GenJson.Round(beats.Confidence),
            ["tempo_within_tolerance"] = beats.WithinTolerance,
            ["beat_count"] = beats.BeatsSeconds.Count,
            ["key"] = track.Key is null ? null : track.Key.Value.Key.ToString(),
            ["key_confidence"] = track.Key is null ? null : GenJson.Round(track.Key.Value.Confidence),
            ["levels"] = levels,
        };
    }

}
