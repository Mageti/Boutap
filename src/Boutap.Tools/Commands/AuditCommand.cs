// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json;
using System.Text.Json.Nodes;
using Boutap.Audio;
using Boutap.Core.Audit;
using Boutap.Core.Pack;

namespace Boutap.Tools.Commands;

/// <summary>Audite un depot : schemas, profils et packs.</summary>
/// <remarks>
/// <para>
/// La CI appelle <c>boutap audit --warn .</c> a chaque commit. C'est le seul
/// endroit ou le code compare les schemas JSON au modele C# : sans cela, un
/// champ ajoute au modele sans etre ajoute au schema passerait tous les tests,
/// puisque les tests passent par le modele.
/// </para>
/// </remarks>
public sealed class AuditCommand : ICommand
{
    /// <inheritdoc/>
    public string Name => "audit";

    /// <inheritdoc/>
    public string Summary => "Audite un depot : schemas, profils et packs.";

    /// <inheritdoc/>
    public string Usage => "boutap audit [--warn] [--json] [--strict] [--no-audio] [chemin...]";

    /// <inheritdoc/>
    public IReadOnlyList<string> Flags => ["warn", "json", "strict", "no-audio"];

    public int Run(CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        IReadOnlyList<string> roots = context.Rest();
        if (roots.Count == 0)
        {
            roots = new[] { "." };
        }

        bool asJson = context.Has("json");
        bool showWarnings = context.Has("warn");
        bool strict = context.Has("strict");
        IPackAudioProbe? probe = context.Has("no-audio") ? null : new WavAudioProbe();

        RepositoryAuditor auditor = new(probe);
        ValidationResult global = new();
        List<AuditSubject> subjects = new();

        foreach (string root in roots)
        {
            subjects.AddRange(auditor.Audit(root, global));
        }

        if (asJson)
        {
            context.Out.WriteLine(AuditJson.Build(subjects, global, showWarnings).ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            return ExitCodeFor(subjects, global, strict);
        }

        foreach (ValidationIssue issue in global.Issues)
        {
            context.Out.WriteLine("  " + issue.Format());
        }

        if (showWarnings)
        {
            foreach (ValidationIssue issue in global.Issues.Where(i => !i.IsError))
            {
                context.Out.WriteLine("  " + issue.Format());
            }
        }

        foreach (AuditSubject subject in subjects)
        {
            int errors = subject.Result.ErrorCount;
            int warnings = subject.Result.WarningCount;

            if (errors == 0 && warnings == 0)
            {
                // Un pack d'un repertoire indexe n'est pas « conforme » au
                // sens du format : il tient sa place dans l'index. Dire
                // « conforme » sans le preciser ferait passer pour sain un
                // pack qui n'existe que pour casser.
                string verdict = subject.Expected
                    ? "conforme a l'index."
                    : "conforme.";
                context.Out.WriteLine($"  {subject.Kind,-7} {subject.Label} : {verdict}");
                continue;
            }

            context.Out.WriteLine($"  {subject.Kind,-7} {subject.Label} : {errors} erreur(s), {warnings} avertissement(s).");

            foreach (ValidationIssue issue in subject.Result.Issues)
            {
                if (issue.IsError || showWarnings)
                {
                    context.Out.WriteLine("      " + issue.Format());
                }
            }
        }

        int totalErrors = global.ErrorCount + subjects.Sum(s => s.Result.ErrorCount);
        int totalWarnings = global.WarningCount + subjects.Sum(s => s.Result.WarningCount);

        context.Out.WriteLine();
        context.Out.WriteLine(
            $"Audit : {subjects.Count} sujet(s), {totalErrors} erreur(s), {totalWarnings} avertissement(s).");

        return ExitCodeFor(subjects, global, strict);
    }

    private static int ExitCodeFor(IReadOnlyList<AuditSubject> subjects, ValidationResult global, bool strict)
    {
        int errors = global.ErrorCount + subjects.Sum(s => s.Result.ErrorCount);
        if (errors > 0)
        {
            return ExitCodes.Invalid;
        }

        int warnings = global.WarningCount + subjects.Sum(s => s.Result.WarningCount);
        return strict && warnings > 0 ? ExitCodes.Failure : ExitCodes.Success;
    }
}

/// <summary>Construit la sortie JSON de l'audit.</summary>
internal static class AuditJson
{
    /// <summary>Assemble l'objet racine de la sortie JSON.</summary>
    public static JsonObject Build(IReadOnlyList<AuditSubject> subjects, ValidationResult global, bool showWarnings)
    {
        ArgumentNullException.ThrowIfNull(subjects);
        ArgumentNullException.ThrowIfNull(global);

        JsonArray items = new();
        foreach (AuditSubject subject in subjects)
        {
            JsonArray issues = new();
            foreach (ValidationIssue issue in subject.Result.Issues)
            {
                if (issue.IsError || showWarnings)
                {
                    issues.Add(new JsonObject
                    {
                        ["severity"] = issue.IsError ? "error" : "warning",
                        ["code"] = issue.Code,
                        ["message"] = issue.Message,
                        ["json_pointer"] = issue.JsonPointer,
                    });
                }
            }

            items.Add(new JsonObject
            {
                ["kind"] = subject.Kind,
                ["path"] = subject.Label,
                ["governed_by_index"] = subject.Expected,
                ["valid"] = subject.Result.IsValid,
                ["errors"] = subject.Result.ErrorCount,
                ["warnings"] = subject.Result.WarningCount,
                ["issues"] = issues,
            });
        }

        return new JsonObject
        {
            ["subjects"] = items,
            ["errors"] = global.ErrorCount + subjects.Sum(s => s.Result.ErrorCount),
            ["warnings"] = global.WarningCount + subjects.Sum(s => s.Result.WarningCount),
        };
    }
}
