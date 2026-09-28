// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Le corps de boutap-gen, separe du point d'entree pour que la logique reste
// testable sans passer par un processus.

using Boutap.Audio;
using Boutap.Core.Common;
using Boutap.Core.Pack;
using Boutap.Gen.Analysis;
using Boutap.Gen.Compose;
using Boutap.Gen.Dsp;

namespace Boutap.Gen.Cli;

/// <summary>Point d'entree de boutap-gen.</summary>
public static class GenProgram
{
    /// <summary>Code de sortie : tout va bien.</summary>
    public const int Success = 0;

    /// <summary>Code de sortie : la commande a echoue.</summary>
    public const int Failure = 1;

    /// <summary>Code de sortie : la ligne de commande n'a pas de sens.</summary>
    public const int UsageError = 2;

    private const string Usage = """
        Usage : boutap-gen <commande> [options]

        Commandes :
          analyse    <audio>              Decrit le morceau : tempo, tonalite, onsets.
          generate   <audio> -o <pack>    Ecrit un pack .btp depuis un WAV.
          levels                          Liste les niveaux et leur densite.

        Options communes :
          --level <niveau>   Ne generer qu'un niveau (berceau, ronde, cascade).
          --json             Sortie machine, une seule ligne.
          --seed <hex>       Impose le materiau de graine (64 chiffres
                            hexadecimaux) : donne une variante du meme
                            morceau, toujours reproductible. L'empreinte de
                            l'audio, elle, ne change pas — elle dit quel
                            audio a ete analyse.
          --dry-run          N'ecrit rien : la generation reste complete.

        L'audio doit etre un WAV lisible par Boutap. Le pack produit est
        verifiable avec « boutap validate ».
""";

    /// <summary>Execute la commande demandee.</summary>
    /// <param name="args">Les arguments de la ligne de commande.</param>
    /// <param name="output">Flux de sortie, injectable pour les tests.</param>
    /// <param name="error">Flux d'erreur, injectable pour les tests.</param>
    public static int Run(IReadOnlyList<string> args, TextWriter? output = null, TextWriter? error = null)
    {
        ArgumentNullException.ThrowIfNull(args);
        TextWriter writer = output ?? Console.Out;
        TextWriter diagnostics = error ?? Console.Error;

        if (args.Count == 0 || args[0] is "-h" or "--help" or "help")
        {
            writer.WriteLine(Usage);
            return args.Count == 0 ? UsageError : Success;
        }

        if (args[0] is "--version" or "version")
        {
            writer.WriteLine($"{ChartGenerator.Name} {SemanticVersion.Current} (AGPL-3.0-or-later)");
            return Success;
        }

        try
        {
            return args[0] switch
            {
                "analyse" => AnalyseCommand.Run(args, writer, diagnostics),
                "generate" => GenerateCommand.Run(args, writer, diagnostics),
                "levels" => LevelsCommand.Run(args, writer),
                _ => Unknown(args[0], diagnostics),
            };
        }
        catch (FileNotFoundException ex)
        {
            diagnostics.WriteLine($"Fichier introuvable : {ex.FileName}");
            return Failure;
        }
        catch (Exception ex) when (ex is WavFormatException or PackFormatException or IOException)
        {
            diagnostics.WriteLine($"Lecture impossible : {ex.Message}");
            return Failure;
        }
        catch (ArgumentException ex)
        {
            diagnostics.WriteLine($"Option invalide : {ex.Message}");
            if (Environment.GetEnvironmentVariable("BOUTAP_GEN_TRACE") == "1")
            {
                diagnostics.WriteLine(ex.ToString());
            }

            return UsageError;
        }
    }

    internal static int Unknown(string command, TextWriter diagnostics)
    {
        diagnostics.WriteLine($"Commande inconnue : « {command} ».");
        diagnostics.WriteLine(Usage);
        return UsageError;
    }

    /// <summary>Lit les options communes a toutes les commandes.</summary>
    /// <param name="args">Les arguments de la ligne de commande.</param>
    /// <param name="start">Premier argument a lire.</param>
    /// <param name="commandOptions">
    /// Options que possede la commande sans qu'elles soient communes. Elles ne
    /// sont pas consommees ici : elles finissent parmi les positionnels, et la
    /// commande les relit avec sa propre lecture. « generate » y declare ainsi
    /// « -o » et « --output », qui neInterested que lui.
    /// </param>
    internal static CommonOptions ReadCommon(IReadOnlyList<string> args, int start, params string[] commandOptions)
    {
        var options = new CommonOptions();
        for (int i = start; i < args.Count; i++)
        {
            string argument = args[i];
            if (argument == "--json")
            {
                options.Json = true;
            }
            else if (argument == "--dry-run")
            {
                options.DryRun = true;
            }
            else if (argument == "--level")
            {
                options.Levels = [ParseLevel(args, ref i)];
            }
            else if (argument == "--seed")
            {
                options.SeedHex = TakeValue(args, ref i, "--seed");
            }
            else if (IsOption(argument) && !commandOptions.Contains(argument, StringComparer.Ordinal))
            {
                // Une option qu'on ne connait pas est presque toujours une faute
                // de frappe. L'ignorer en silence faisait qu'un « --tempo 100 »
                // ou un « --levl ronde » passaient pour un succes complet, et
                // que rien dans la sortie ne pouvait permettre de le remarquer.
                // Le message ne commence pas par « Option inconnue » : Run le
                // préfixe déjà par « Option invalide : ».
                throw new ArgumentException(
                    $"{argument} n'est pas une option de boutap-gen. « boutap-gen --help » donne la liste.");
            }
            else
            {
                options.Positionals.Add(argument);
            }
        }

        if (options.SeedHex is not null && !Sha256Hex.IsLowerHex64(options.SeedHex))
        {
            throw new ArgumentException("--seed attend 64 chiffres hexadecimaux, pas « " + options.SeedHex + " ».");
        }

        return options;
    }

    /// <summary>Un argument qui porte un tiret est une option, pas un nom de fichier.</summary>
    private static bool IsOption(string argument) => argument.Length > 1 && argument[0] == '-';

    /// <summary>Prend la valeur d'une option, et avance sur elle.</summary>
    /// <param name="args">Les arguments de la ligne de commande.</param>
    /// <param name="i">Position de l'option ; avancee d'un cran.</param>
    /// <param name="option">Le nom de l'option, pour le message d'erreur.</param>
    /// <remarks>
    /// Une option qui attend une valeur et n'en a pas est une faute de frappe.
    /// La traiter comme une option inconnue — c'est-a-dire l'ignorer — faisait
    /// qu'un « --level » nu produisait les trois niveaux sans rien dire, et
    /// qu'un « --seed » nu laissait la graine tiree au sort. Une valeur qui
    /// commence par deux tirets n'est pas une valeur : c'est l'option suivante.
    /// </remarks>
    private static string TakeValue(IReadOnlyList<string> args, ref int i, string option)
    {
        if (i + 1 >= args.Count || args[i + 1].StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException($"{option} attend une valeur, et il n'y en a pas.");
        }

        i++;
        return args[i];
    }

    /// <summary>Prend le nom d'un niveau, et avance sur lui.</summary>
    /// <param name="args">Les arguments de la ligne de commande.</param>
    /// <param name="i">Position de l'option ; avancee d'un cran.</param>
    /// <remarks>
    /// <see cref="ChartLevels.Parse"/> leve une <see cref="FormatException"/>,
    /// que <see cref="Run"/> ne rattrape pas : sans cette conversion, un niveau
    /// mal ecrit tuait le programme avec la pile d'appels sur l'ecran et un
    /// code de sortie 134. Le message, lui, est deja le bon.
    /// </remarks>
    private static ChartLevel ParseLevel(IReadOnlyList<string> args, ref int i)
    {
        string name = TakeValue(args, ref i, "--level");
        try
        {
            return ChartLevels.Parse(name);
        }
        catch (FormatException ex)
        {
            throw new ArgumentException(ex.Message);
        }
    }

    /// <summary>Les options que toutes les commandes partagent.</summary>
    internal sealed class CommonOptions
    {
        /// <summary>Sortie machine.</summary>
        public bool Json { get; set; }

        /// <summary>Ne rien ecrire.</summary>
        public bool DryRun { get; set; }

        /// <summary>Niveaux demandes, ou <see langword="null"/> pour les trois.</summary>
        public IReadOnlyList<ChartLevel>? Levels { get; set; }

        /// <summary>Graine forcee, ou <see langword="null"/> pour la derive.</summary>
        public string? SeedHex { get; set; }

        /// <summary>Arguments qui ne sont pas des options.</summary>
        public List<string> Positionals { get; } = [];
    }

    internal static IReadOnlyList<LevelProfile> ProfilesFor(IReadOnlyList<ChartLevel>? levels)
    {
        if (levels is null)
        {
            return ChartGeneratorOptions.DefaultProfiles;
        }

        var profiles = new List<LevelProfile>();
        foreach (LevelProfile defaultProfile in ChartGeneratorOptions.DefaultProfiles)
        {
            foreach (ChartLevel level in levels)
            {
                if (defaultProfile.Level == level)
                {
                    profiles.Add(defaultProfile);
                }
            }
        }

        return profiles;
    }
}
