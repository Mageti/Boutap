// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Boutap.Tools;

internal static class Program
{
    private static int Main(string[] args)
    {
        TextWriter output = Console.Out;
        TextWriter error = Console.Error;

        // Lu avant le bloc try : une ligne de commande illisible doit
        // pouvoir, elle aussi, etre decrite en detail sur demande.
        bool verbose = false;

        try
        {
            ParsedCommandLine parsed = CommandLine.Parse(args);
            verbose = parsed.Has("verbose") || parsed.Has("debug");

            // « --version » et « --help » ne sont des commandes que si aucune
            // commande n'a ete nommee. Sinon « boutap validate --version »
            // afficherait une version au lieu de valider, et « --help » ne
            // servirait qu'a afficher l'aide generale alors que l'appelant
            // demandait celle de « validate ».
            if (parsed.ShowHelp)
            {
                WriteHelp(output, ResolveHelpTopic(parsed));
                return ExitCodes.Success;
            }

            if (parsed.Command is null)
            {
                if (parsed.Has("version"))
                {
                    parsed = parsed with { Command = "version" };
                }
                else if (args.Length == 0)
                {
                    WriteHelp(output, topic: null);
                    return ExitCodes.Success;
                }
                else
                {
                    error.WriteLine($"{ToolInfo.Name} : aucune commande.");
                    error.WriteLine();
                    WriteCommandList(error);
                    return ExitCodes.UsageError;
                }
            }

            ICommand? command = CommandRegistry.Find(parsed.Command);
            if (command is null)
            {
                error.WriteLine($"{ToolInfo.Name} : commande inconnue « {parsed.Command} ».");
                error.WriteLine();
                WriteCommandList(error);
                return ExitCodes.UsageError;
            }

            CommandContext context = new(parsed, output, error);
            return command.Run(context);
        }
        catch (CommandLineException ex)
        {
            error.WriteLine($"{ToolInfo.Name} : {ex.Message}");
            error.WriteLine("Essayez « boutap about » pour la liste des commandes.");
            return ExitCodes.UsageError;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            error.WriteLine($"{ToolInfo.Name} : {ex.GetType().Name} : {ex.Message}");
            if (verbose)
            {
                error.WriteLine(ex.ToString());
            }

            return ExitCodes.Failure;
        }
    }

    /// <summary>De quelle commande l'utilisateur demande-t-il l'aide ?</summary>
    /// <param name="parsed">Ligne de commande analysee.</param>
    /// <returns>Le nom de la commande visee, ou <see langword="null"/>.</returns>
    private static string? ResolveHelpTopic(ParsedCommandLine parsed)
    {
        string? command = parsed.Command;

        if (command is null or "-h")
        {
            // « boutap -h validate » et « boutap help validate » : le nom de
            // la commande visee est le second positionnel.
            return parsed.Positionals.Count > 1 ? parsed.Positionals[1] : null;
        }

        return command is "help"
            ? (parsed.Positionals.Count > 1 ? parsed.Positionals[1] : null)
            : command;
    }

    private static void WriteHelp(TextWriter writer, string? topic)
    {
        if (topic is not null)
        {
            if (CommandRegistry.Find(topic) is ICommand command)
            {
                writer.WriteLine($"{ToolInfo.Name} {command.Name} — {command.Summary}");
                writer.WriteLine();
                writer.WriteLine("Usage :");
                writer.WriteLine($"  {command.Usage}");
                return;
            }

            writer.WriteLine($"{ToolInfo.Name} : commande inconnue « {topic} ».");
            writer.WriteLine();
        }

        writer.WriteLine($"{ToolInfo.Name} {ToolInfo.Version} — {ToolInfo.Description}");
        writer.WriteLine();
        writer.WriteLine("Usage :");
        writer.WriteLine($"  {ToolInfo.Name} <commande> [options] [arguments]");
        writer.WriteLine();
        WriteCommandList(writer);
        writer.WriteLine();
        writer.WriteLine("Options generales :");
        writer.WriteLine("  --version        Affiche la version et sort.");
        writer.WriteLine("  --help           Affiche cette aide, ou l'aide d'une commande.");
        writer.WriteLine("  --verbose        Detailed l'erreur en cas d'incident.");
        writer.WriteLine("  --               Ce qui suit est un argument, meme commencant par --");
    }

    private static void WriteCommandList(TextWriter writer)
    {
        writer.WriteLine("Commandes :");
        foreach (ICommand command in CommandRegistry.All)
        {
            writer.WriteLine($"  {command.Name,-14} {command.Summary}");
        }
    }
}
