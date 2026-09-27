// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Globalization;

namespace Boutap.Tools;

/// <summary>
/// Lecteur de ligne de commande, ecrit pour ce projet et sans dependance.
/// </summary>
/// <remarks>
/// <para>
/// Pas de paquet NuGet, deliberement : <c>boutap</c> doit rester copiable
/// tel quel sur la machine d'un joueur, sur celle d'un mainteneur et dans une
/// image de CI, sans <c>dotnet restore</c> derriere. Un analyseur d'arguments
/// fait dix lignes de plus que ce qu'il apporte.
/// </para>
/// <para>
/// Formes acceptees : <c>commande sous-commande --option valeur --option=valeur
/// --drapeau positionnel</c>. Le premier argument non option est la commande.
/// Un <c>--</c> termine l'analyse des options, ce qui permet de passer un nom
/// de fichier qui commence par un tiret.
/// </para>
/// <para>
/// Un tiret unique n'ouvre pas d'option : <c>-h</c> est un positionnel, ce
/// qui laisse <c>--flag</c> et <c>-h</c> coexister sans que le premier qui
/// passe soit gagne. C'est un choix, et il est inhabituel pour Unix, ou
/// <c>-h</c> est une option ; il est fait ici dans l'autre sens, parce que
/// les noms de fichiers d'un pack commencent parfois par un tiret.
/// </para>
/// </remarks>
public static class CommandLine
{
    /// <summary>Prefixe des options nommees.</summary>
    public const string OptionPrefix = "--";

    /// <summary>Ce qui termine l'analyse des options.</summary>
    public const string Terminator = "--";

    /// <summary>Options et positionnels qui demandent l'aide.</summary>
    private static readonly string[] HelpKeywords = ["-h", "--help", "help"];

    /// <summary>Decoupe une ligne de commande.</summary>
    /// <param name="args">Arguments recus par le processus.</param>
    /// <returns>La commande, les positionnels et les options.</returns>
    /// <exception cref="CommandLineException">
    /// Une option a une valeur mais ne recoit rien, ou un <c>--</c> a ete
    /// utilise comme option.
    /// </exception>
    public static ParsedCommandLine Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        List<string> positionals = new();
        Dictionary<string, string?> options = new(StringComparer.Ordinal);
        bool optionsEnded = false;
        int index = 0;

        while (index < args.Count)
        {
            string arg = args[index];
            index++;

            if (optionsEnded)
            {
                positionals.Add(arg);
                continue;
            }

            if (arg == Terminator)
            {
                optionsEnded = true;
                continue;
            }

            if (!arg.StartsWith(OptionPrefix, StringComparison.Ordinal))
            {
                positionals.Add(arg);
                continue;
            }

            string body = arg[OptionPrefix.Length..];
            if (body.Length == 0)
            {
                throw new CommandLineException("L'option « -- » n'est pas une option.");
            }

            int equals = body.IndexOf('=', StringComparison.Ordinal);
            if (equals >= 0)
            {
                string name = body[..equals];
                string value = body[(equals + 1)..];
                EnsureName(name);
                options[name] = value;
                continue;
            }

            EnsureName(body);

            // Une option suivante, ou la fin des arguments, signifie que
            // l'option est un drapeau. Sinon, la valeur suivante est la
            // sienne.
            bool nextIsValue = index < args.Count && !args[index].StartsWith(OptionPrefix, StringComparison.Ordinal);
            if (nextIsValue)
            {
                options[body] = args[index];
                index++;
            }
            else
            {
                options[body] = null;
            }
        }

        string? command = positionals.Count > 0 ? positionals[0] : null;
        bool showHelp = options.ContainsKey("help")
            || (positionals.Count > 0 && Array.IndexOf(HelpKeywords, positionals[0]) >= 0);

        return new ParsedCommandLine(command, positionals, options, showHelp);
    }

    /// <summary>
    /// Analyse un entier avec la culture invariante, comme l'exige un outil
    /// qui rend des chaines destinees a etre relues par une chaine d'appel.
    /// </summary>
    /// <param name="text">Texte a analyser.</param>
    /// <param name="value">Entier analyse.</param>
    /// <returns>Vrai si le texte est un entier.</returns>
    public static bool TryParseInt32(string? text, out int value) =>
        int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);

    private static void EnsureName(string name)
    {
        if (name.Length == 0)
        {
            throw new CommandLineException("Le nom d'une option ne peut pas etre vide.");
        }

        foreach (char c in name)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '-')
            {
                throw new CommandLineException(
                    $"Le nom d'option « {name} » contient « {c} ». Lettres, chiffres et tirets seulement.");
            }
        }
    }
}
