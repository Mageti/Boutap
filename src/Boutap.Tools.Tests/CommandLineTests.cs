// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Boutap.Core.Common;
using Boutap.Tools;
using Xunit;

namespace Boutap.Tools.Tests;

/// <summary>Analyse de la ligne de commande, sans dependance externe.</summary>
public sealed class CommandLineTests
{
    [Fact]
    public void Ligne_Vide_N_A_Ni_Commande_Ni_Option()
    {
        ParsedCommandLine parsed = Parse();

        Assert.Null(parsed.Command);
        Assert.Empty(parsed.Positionals);
        Assert.Empty(parsed.Options);
        Assert.False(parsed.ShowHelp);
    }

    [Fact]
    public void Premier_Positionnel_Est_La_Commande()
    {
        ParsedCommandLine parsed = Parse("validate", "packs/demo.btp");

        Assert.Equal("validate", parsed.Command);
        Assert.Equal(["packs/demo.btp"], parsed.Positionals.Skip(1).ToArray());
    }

    [Fact]
    public void Option_Suivie_D_Une_Valeur_Les_Prend()
    {
        ParsedCommandLine parsed = Parse("validate", "--iterations", "5");

        Assert.True(parsed.Has("iterations"));
        Assert.True(parsed.TryGetValue("iterations", out string? value));
        Assert.Equal("5", value);
    }

    [Fact]
    public void Option_Avec_Signe_Egale_Est_Acceptee()
    {
        ParsedCommandLine parsed = Parse("validate", "--iterations=5", "--chemin=/tmp/pack.btp");

        Assert.Equal("5", parsed.ValueOr("iterations", null));
        Assert.Equal("/tmp/pack.btp", parsed.ValueOr("chemin", null));
    }

    [Fact]
    public void Option_Sans_Valeur_Est_Un_Drapeau()
    {
        ParsedCommandLine parsed = Parse("audit", "--warn");

        Assert.True(parsed.Has("warn"));
        Assert.False(parsed.TryGetValue("warn", out string? value));
        Assert.Null(value);
    }

    [Fact]
    public void Option_Suivie_D_Une_Autre_Option_Est_Un_Drapeau()
    {
        ParsedCommandLine parsed = Parse("audit", "--warn", "--json");

        Assert.True(parsed.Has("warn"));
        Assert.True(parsed.Has("json"));
        Assert.False(parsed.TryGetValue("warn", out _));
    }

    [Fact]
    public void Une_Valeur_Negative_N_Est_Pas_Prise_Pour_Une_Option()
    {
        ParsedCommandLine parsed = Parse("validate", "--offset", "-1");

        Assert.Equal("-1", parsed.ValueOr("offset", null));
    }

    [Fact]
    public void Terminateur_Transforme_Le_Reste_En_Positionnels()
    {
        // Un nom de fichier peut commencer par un tiret : c'est tout l'objet
        // du « -- ».
        ParsedCommandLine parsed = Parse("validate", "--", "--pas-une-option.btp");

        Assert.Equal("validate", parsed.Command);
        Assert.Equal(["--pas-une-option.btp"], parsed.Positionals.Skip(1).ToArray());
        Assert.False(parsed.Has("pas-une-option.btp"));
    }

    [Fact]
    public void Option_A_Nom_Vide_Refusee()
    {
        Assert.Throws<CommandLineException>(() => Parse("validate", "--=5"));
    }

    [Theory]
    [InlineData("--avec_espace")]
    [InlineData("--avec/slash")]
    [InlineData("--avec\\antislash")]
    public void Option_A_Nom_Etrange_Refusee(string argument)
    {
        Assert.Throws<CommandLineException>(() => Parse("validate", argument));
    }

    [Theory]
    [InlineData("-h")]
    [InlineData("--help")]
    [InlineData("help")]
    public void Demande_D_Aide_Est_Repercee(string argument)
    {
        Assert.True(Parse(argument).ShowHelp);
    }

    [Theory]
    [InlineData("validate")]
    [InlineData("audit", "--warn")]
    [InlineData("validate", "--iterations", "5")]
    [InlineData("validate", "--", "a.btp")]
    public void Absence_D_Demande_D_Aide_Est_Repercee(params string[] args)
    {
        Assert.False(Parse(args).ShowHelp);
    }

    [Fact]
    public void Aide_D_Une_Commande_Nommee_N_Est_Pas_La_Demande_Generale()
    {
        Assert.Equal("validate", Parse("validate", "--help").Command);
    }

    [Fact]
    public void Valeur_De_Repli_Est_Rendue_Si_L_Option_Est_Absente()
    {
        ParsedCommandLine parsed = Parse("validate");

        Assert.Equal("repli", parsed.ValueOr("absent", "repli"));
        Assert.Null(parsed.ValueOr("absent", null));
    }

    [Fact]
    public void Valeur_De_Repli_N_Ecrase_Pas_Une_Valeur_Vide()
    {
        // « --chemin= » est une intention : le chemin est la racine, pas la
        // chaine vide.
        ParsedCommandLine parsed = Parse("validate", "--chemin=");

        Assert.Equal(string.Empty, parsed.ValueOr("chemin", "repli"));
    }

    [Theory]
    [InlineData("5", 5)]
    [InlineData("-5", -5)]
    [InlineData("+5", 5)]
    [InlineData("0", 0)]
    public void Entiers_Analyses_Invariablement_Quel_Que_Soit_L_Environment(string text, int expected)
    {
        Assert.True(CommandLine.TryParseInt32(text, out int value));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("cinq")]
    [InlineData("5,0")]
    [InlineData("5.0")]
    [InlineData(null)]
    [InlineData(" 5 ")]
    public void Ce_Qui_N_Est_Pas_Un_Entier_Refuse(string? text)
    {
        Assert.False(CommandLine.TryParseInt32(text, out _));
    }

    [Fact]
    public void Analyse_Refuse_Une_Liste_Nulle()
    {
        Assert.Throws<ArgumentNullException>(() => CommandLine.Parse(null!));
    }

    private static ParsedCommandLine Parse(params string[] args) => CommandLine.Parse(args);
}

/// <summary>Le contexte transmis aux commandes.</summary>
public sealed class CommandContextTests
{
    [Fact]
    public void Rest_Retire_La_Commande_Par_Defaut()
    {
        CommandContext context = Build(out _, out _, "validate", "a.btp", "b.btp");

        Assert.Equal(["a.btp", "b.btp"], context.Rest());
    }

    [Fact]
    public void Rest_Peut_Retirer_La_Sous_Commande()
    {
        CommandContext context = Build(out _, out _, "pack", "audit", "a.btp");

        Assert.Equal(["a.btp"], context.Rest(2));
    }

    [Fact]
    public void Rest_D_Au_Dela_De_Ce_Qu_Il_Reste_Est_Vide()
    {
        CommandContext context = Build(out _, out _, "validate");

        Assert.Empty(context.Rest());
        Assert.Empty(context.Rest(5));
    }

    [Fact]
    public void Avertissement_Arrive_Sur_Le_Flux_D_Erreur()
    {
        CommandContext context = Build(out StringWriter output, out StringWriter error, "audit");

        context.Warn("avertissement");

        Assert.Empty(output.ToString());
        Assert.Contains("avertissement", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Les_Options_Sont_Lues_A_Travers_Le_Contexte()
    {
        CommandContext context = Build(out _, out _, "audit", "--warn", "packs");

        Assert.True(context.Has("warn"));
        Assert.Equal("packs", context.ValueOr("warn", null));
    }

    [Fact]
    public void Contexte_Refuse_Des_Flux_Nuls()
    {
        ParsedCommandLine parsed = CommandLine.Parse(["audit"]);

        Assert.Throws<ArgumentNullException>(
            () => new CommandContext(parsed, null!, new StringWriter()));
        Assert.Throws<ArgumentNullException>(
            () => new CommandContext(parsed, new StringWriter(), null!));
        Assert.Throws<ArgumentNullException>(
            () => new CommandContext(null!, new StringWriter(), new StringWriter()));
    }

    private static CommandContext Build(
        out StringWriter output,
        out StringWriter error,
        params string[] args)
    {
        ParsedCommandLine parsed = CommandLine.Parse(args);
        output = new StringWriter();
        error = new StringWriter();
        return new CommandContext(parsed, output, error);
    }
}

/// <summary>Le registre des commandes et l'identite de l'outil.</summary>
public sealed class CommandRegistryTests
{
    [Fact]
    public void Chaque_Commande_Est_Trouvee_Son_Nom()
    {
        foreach (ICommand command in CommandRegistry.All)
        {
            Assert.Same(command, CommandRegistry.Find(command.Name));
        }
    }

    [Fact]
    public void Les_Noms_Sont_Unique()
    {
        string[] names = CommandRegistry.All.Select(c => c.Name).ToArray();

        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Une_Commande_Inconnue_Rend_Null_Et_N_Leve_Pas()
    {
        Assert.Null(CommandRegistry.Find("valider"));
        Assert.Null(CommandRegistry.Find(""));
        Assert.Null(CommandRegistry.Find(null));
    }

    [Fact]
    public void La_Recherche_Est_Sensible_A_La_Casse()
    {
        Assert.Null(CommandRegistry.Find("Version"));
    }

    [Fact]
    public void Chaque_Commande_Dit_Ce_Qu_Elle_Fait_Et_Comment_L_Appeler()
    {
        foreach (ICommand command in CommandRegistry.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(command.Name));
            Assert.False(string.IsNullOrWhiteSpace(command.Summary), command.Name);
            Assert.False(string.IsNullOrWhiteSpace(command.Usage), command.Name);
            Assert.StartsWith("boutap ", command.Usage, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void L_Outil_S_Edit_Lui_Meme_Et_Porte_Une_Version_Exploitable()
    {
        Assert.Equal("boutap", ToolInfo.Name);
        Assert.Equal("AGPL-3.0-or-later", ToolInfo.License);
        Assert.StartsWith("https://", ToolInfo.RepositoryUrl, StringComparison.Ordinal);
    }

    [Fact]
    public void La_Version_De_L_Outil_Est_Celle_Du_Depot()
    {
        // Trois endroits decomposer la version finissent toujours par
        // diverger ; cet outil en a un seul, et il doit dire 0.1.0.
        SemanticVersion built = Parse(ToolInfo.Version);

        Assert.Equal(SemanticVersion.Current, built);
    }

    [Fact]
    public void Les_Codes_De_Sortie_Sont_Distincts()
    {
        int[] codes = [ExitCodes.Success, ExitCodes.Failure, ExitCodes.UsageError, ExitCodes.Invalid];

        Assert.Equal(codes.Length, codes.Distinct().Count());
        Assert.Equal(0, ExitCodes.Success);
    }

    [Fact]
    public void Les_Codes_D_Erreur_Sont_Positifs()
    {
        // 0 est le seul code qui signifie « reussite » : un shell qui
        // s'appuie sur « non nul » doit pouvoir s'y fier.
        Assert.True(ExitCodes.Failure > 0);
        Assert.True(ExitCodes.UsageError > 0);
        Assert.True(ExitCodes.Invalid > 0);
    }

    private static SemanticVersion Parse(string text) => SemanticVersion.Parse(text);
}
