// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json.Nodes;
using Boutap.Audio;
using Boutap.Core.Pack;

namespace Boutap.Tools.Commands;

/// <summary>Verifie qu'un ou plusieurs packs sont conformes au format.</summary>
/// <remarks>
/// <para>
/// C'est la commande qu'un auteur de pack lance avant de publier. Elle ne se
/// contente pas a dire si le pack est valide : elle rend tous les constats
/// d'un coup, avec un code stable et un pointeur JSON vers le champ fautif,
/// pour qu'on n'ait pas a rejouer dix fois le validateur pour trouver le
/// dixieme probleme.
/// </para>
/// <para>
/// Avec <c>--json</c>, la sortie standard ne contient que le resultat : un
/// objet JSON par pack, une ligne chacun. Tout le reste — la banniere, le
/// resume, les messages d'erreur — va sur la sortie d'erreur, pour qu'un
/// appelant puisse faire <c>validate --json *.btp | jq</c> sans le rebut.
/// </para>
/// </remarks>
public sealed class ValidateCommand : ICommand
{
    /// <inheritdoc/>
    public string Name => "validate";

    /// <inheritdoc/>
    public string Summary => "Verifie qu'un ou plusieurs packs sont conformes au format.";

    /// <inheritdoc/>
    public string Usage => "boutap validate <pack.btp>... [--json] [--strict] [--no-audio]";

    /// <inheritdoc/>
    public IReadOnlyList<string> Flags => ["json", "strict", "no-audio"];

    public int Run(CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        IReadOnlyList<string> paths = context.Rest();
        if (paths.Count == 0)
        {
            context.Warn("Usage : boutap validate <pack.btp>... [--json] [--strict] [--no-audio]");
            return ExitCodes.UsageError;
        }

        bool asJson = context.Has("json");
        bool strict = context.Has("strict");
        IPackAudioProbe? probe = context.Has("no-audio") ? null : new WavAudioProbe();

        // Sur la sortie d'erreur, jamais sur la sortie standard : avec --json,
        // la sortie standard doit pouvoir etre capturee et analysee telle
        // quelle par un appelant qui n'a pas demande de le lire.
        if (probe is not null)
        {
            context.Note($"Lecture audio : {WavAudioProbe.Name} ({WavAudioProbe.CanonicalFormDescription}).");
        }
        else
        {
            context.Note("Lecture audio desactivee : audio.sha256 ne sera pas verifie.");
        }

        context.Note(string.Empty);

        int invalid = 0;

        foreach (string path in paths)
        {
            if (!File.Exists(path))
            {
                context.Warn($"« {path} » n'existe pas.");
                invalid++;
                continue;
            }

            if (!ValidateOne(context, path, probe, asJson))
            {
                invalid++;
            }
        }

        if (paths.Count > 1)
        {
            context.Note($"{paths.Count - invalid} pack(s) conforme(s) sur {paths.Count}.");
        }

        if (invalid == 0)
        {
            return ExitCodes.Success;
        }

        return strict ? ExitCodes.Failure : ExitCodes.Invalid;
    }

    private static bool ValidateOne(CommandContext context, string path, IPackAudioProbe? probe, bool asJson)
    {
        LoadedPack pack;
        try
        {
            pack = PackReader.Read(path);
        }
        catch (PackFormatException ex)
        {
            context.Warn($"{path} : {ex.Message}");
            return false;
        }
        catch (IOException ex)
        {
            context.Warn($"{path} est illisible : {ex.Message}");
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            context.Warn($"{path} est inaccessible : {ex.Message}");
            return false;
        }

        using (pack)
        {
            ValidationResult result = PackValidator.Validate(pack, probe);
            Report(context, path, result, asJson);
            return result.IsValid;
        }
    }

    private static JsonObject ToJson(string path, ValidationResult result)
    {
        JsonArray issues = new();
        foreach (ValidationIssue issue in result.Issues)
        {
            issues.Add(new JsonObject
            {
                ["severity"] = issue.IsError ? "error" : "warning",
                ["code"] = issue.Code,
                ["message"] = issue.Message,
                ["json_pointer"] = issue.JsonPointer,
            });
        }

        return new JsonObject
        {
            ["pack"] = path,
            ["valid"] = result.IsValid,
            ["errors"] = result.ErrorCount,
            ["warnings"] = result.WarningCount,
            ["issues"] = issues,
        };
    }

    private static void Report(CommandContext context, string path, ValidationResult result, bool asJson)
    {
        if (asJson)
        {
            context.Out.WriteLine(ToJson(path, result).ToJsonString());
            return;
        }

        if (result.IsValid)
        {
            context.Out.WriteLine($"{path} : conforme ({result.WarningCount} avertissement(s)).");
        }
        else
        {
            context.Out.WriteLine($"{path} : {result.ErrorCount} erreur(s), {result.WarningCount} avertissement(s).");
        }

        foreach (ValidationIssue issue in result.Issues)
        {
            context.Out.WriteLine("  " + issue.Format());
        }
    }
}
