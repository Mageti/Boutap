// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Boutap.Core.Tests;
using Xunit;

namespace Boutap.Game.Tests;

/// <summary>
/// Regle 5 de wiki: spec.md 7.3 — le jeu ne connait pas le generateur.
/// </summary>
/// <remarks>
/// <para>
/// Cette regle est difficile a tenir parce qu'elle ne se voit pas : rien ne
/// interdit d'ecrire <c>using Boutap.Gen;</c>, la compilation reussit, et le
/// couplage n'apparait qu'a la lecture du <c>.csproj</c> — trop tard, quand
/// quelqu'un a deja suppose que le jeu pouvait appeler son propre generateur.
/// </para>
/// <para>
/// Le test lit donc la <em>table des references d'assemblies</em> du binaire
/// compile, pas les fichiers de projet. C'est la seule forme de la preuve qui
/// ne depend pas de l'ecriture du developpeur.
/// </para>
/// </remarks>
public sealed class AssemblyRuleTests
{
    private const string GeneratorAssembly = "Boutap.Gen";

    [Fact]
    public void Le_Projet_De_Jeu_Ne_Reference_Pas_Le_Generateur()
    {
        // L'assemblage du projet teste est deja copie dans le dossier de sortie
        // des tests, donc cette preuve est toujours disponible.
        string assembly = Path.Combine(AppContext.BaseDirectory, "Boutap.Game.dll");
        Assert.True(File.Exists(assembly), $"Assembly introuvable : {assembly}");

        IReadOnlyList<string> references = AssemblyReferences(assembly);
        Assert.DoesNotContain(GeneratorAssembly, references, StringComparer.Ordinal);
    }

    [Fact]
    public void La_Coque_Godot_Ne_Reference_Pas_Le_Generateur()
    {
        // La coque Godot n'est pas compilee par Boutap.sln : sur une machine
        // ou le projet n'a jamais ete ouvert, l'assembly n'existe pas. On
        // bascule alors sur le fichier de projet, qui reste une preuve
        // suffisante tant que le binaire n'a pas ete produit.
        string? assembly = FindShellAssembly();
        if (assembly is null)
        {
            AssertNoGeneratorReference(Path.Combine(TestPaths.RepositoryRoot, "game", "Boutap.Shell.csproj"));
            return;
        }

        Assert.DoesNotContain(GeneratorAssembly, AssemblyReferences(assembly), StringComparer.Ordinal);
    }

    [Theory]
    [InlineData("Boutap.Core")]
    [InlineData("Boutap.Audio")]
    [InlineData("Boutap.Input")]
    public void Les_Briques_Communes_Ne_Reference_Nullet_Le_Generateur(string projet)
    {
        // Regle 5 : le generateur ne se trouve dans le joueur que par accident,
        // pas par dependance. La verification est faite sur le fichier de
        // projet parce que ces trois assemblages n'ont pas besoin d'etre
        // charges pour repondre a la question.
        string path = Path.Combine(TestPaths.RepositoryRoot, "src", projet, projet + ".csproj");
        AssertNoGeneratorReference(path);
    }

    [Fact]
    public void Les_Outils_Ont_Le_Droit_De_Connaitre_Le_Generateur()
    {
        // Contre-exemple volontaire : c'est precisement le role de boutap de
        // lancer le generateur. Ecrire ce test empeche de « corriger » la regle
        // en supprimant la seule dependance legitime du depot.
        string path = Path.Combine(TestPaths.RepositoryRoot, "src", "Boutap.Tools", "Boutap.Tools.csproj");
        Assert.Contains(GeneratorAssembly, File.ReadAllText(path), StringComparison.Ordinal);
    }

    private static void AssertNoGeneratorReference(string projectPath)
    {
        Assert.True(File.Exists(projectPath), $"Projet introuvable : {projectPath}");
        string text = File.ReadAllText(projectPath);
        Assert.DoesNotContain(GeneratorAssembly, text, StringComparison.Ordinal);
    }

    private static string? FindShellAssembly()
    {
        string game = Path.Combine(TestPaths.RepositoryRoot, "game");
        if (!Directory.Exists(game))
        {
            return null;
        }

        return Directory
            .EnumerateFiles(game, "Boutap.Shell.dll", SearchOption.AllDirectories)
            .FirstOrDefault(path => !path.Contains($"{Path.DirectorySeparatorChar}ref{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
    }

    private static List<string> AssemblyReferences(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using PEReader pe = new(stream);
        MetadataReader metadata = pe.GetMetadataReader();

        List<string> names = [];
        foreach (AssemblyReferenceHandle handle in metadata.AssemblyReferences)
        {
            AssemblyReference reference = metadata.GetAssemblyReference(handle);
            names.Add(metadata.GetString(reference.Name));
        }

        return names;
    }
}
