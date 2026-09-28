// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
//
// « boutap-gen levels » : que change le niveau de difficulte ?

using System.Text.Json.Nodes;
using Boutap.Gen.Analysis;
using Boutap.Gen.Compose;

using System.Globalization;

namespace Boutap.Gen.Cli;

/// <summary>Decrit les trois niveaux.</summary>
internal static class LevelsCommand
{
    /// <summary>Execute la commande.</summary>
    /// <param name="output">Flux de sortie.</param>
    public static int Run(TextWriter output)
    {
        var lines = new List<string>
        {
            string.Format(CultureInfo.InvariantCulture, "{0,-10} {1,24}", "Niveau", "Subdivisions jouables"),
            new string('-', 36),
        };

        foreach (LevelProfile profile in ChartGeneratorOptions.DefaultProfiles)
        {
            string name = GenJson.Level(profile.Level);
            int step = Math.Max(1, Quantizer.SubdivisionsPerBeat / profile.KeptSubdivisions);
            lines.Add(string.Format(
                CultureInfo.InvariantCulture,
                "{0,-10} {1,8} sur {2,2}  (une sur {3})",
                name,
                profile.KeptSubdivisions,
                Quantizer.SubdivisionsPerBeat,
                step));
        }

        lines.Add(string.Empty);
        lines.Add("Les trois niveaux jouent le meme morceau, lu autrement :");
        lines.Add("  berceau  ne joue que le temps fort ;");
        lines.Add("  ronde    ajoute les contre-temps ;");
        lines.Add("  cascade  accepte la syncope.");
        lines.Add("Aucun niveau n'est une version allegée du precedent.");

        foreach (string line in lines)
        {
            output.WriteLine(line);
        }

        return GenProgram.Success;
    }

    /// <summary>La meme description, en JSON.</summary>
    /// <param name="output">Flux de sortie.</param>
    public static int RunJson(TextWriter output)
    {
        var rows = new JsonArray();
        foreach (LevelProfile profile in ChartGeneratorOptions.DefaultProfiles)
        {
            rows.Add(new JsonObject
            {
                ["level"] = GenJson.Level(profile.Level),
                ["kept_subdivisions"] = profile.KeptSubdivisions,
                ["subdivisions_per_beat"] = Quantizer.SubdivisionsPerBeat,
            });
        }

        output.WriteLine(new JsonObject
        {
            ["schema"] = "boutap/gen-levels/1",
            ["subdivisions_per_beat"] = Quantizer.SubdivisionsPerBeat,
            ["pitch_classes"] = SpectralAnalysis.ClassCount,
            ["levels"] = rows,
        }.ToJsonString(GenJson.Compact));

        return GenProgram.Success;
    }
}
