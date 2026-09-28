// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Boutap.Core.Tests;

/// <summary>Emplacement du depot et des donnees de test.</summary>
/// <remarks>
/// Les tests lisent des fichiers du depot — schemas, profils, fixtures — parce
/// que ces fichiers sont eux-memes du code : les modifier casse le contrat.
/// <c>TEST_TMP</c> n'existe que pour ce qu'on ecrit, jamais pour ce qu'on lit.
/// </remarks>
public static class TestPaths
{
    /// <summary>Racine du depot, reperee par <c>Boutap.sln</c>.</summary>
    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    /// <summary>Repertoire des schemas JSON du format.</summary>
    public static string SchemaDirectory { get; } = Path.Combine(RepositoryRoot, "schema");

    /// <summary>Repertoire des profils de manette.</summary>
    public static string ProfilesDirectory { get; } = Path.Combine(RepositoryRoot, "profiles");

    /// <summary>Repertoire des packs de test, avec leur index d'attentes.</summary>
    public static string FixturesDirectory { get; } = Path.Combine(RepositoryRoot, "tests", "data", "fixtures");

    /// <summary>Pack de demonstration.</summary>
    public static string DemoPackPath { get; } = Path.Combine(RepositoryRoot, "tests", "data", "demo.btp");

    /// <summary>Cree un repertoire temporaire vide, et rend son chemin.</summary>
    public static string NewTemporaryDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "boutap-tests",
            Guid.NewGuid().ToString("n")); // R1-allow: nom unique de repertoire temporaire, hors du jeu
        Directory.CreateDirectory(path);
        return path;
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Boutap.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            "Racine du depot introuvable en remontant depuis " + AppContext.BaseDirectory
            + " : aucun Boutap.sln. Les tests lisent des fichiers du depot, ils ne peuvent pas tourner hors du depot.");
    }
}
