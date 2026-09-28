// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Boutap.Core.Pack;

namespace Boutap.Core.Audit;

/// <summary>Parcourt un depot et audite ce qui s'y trouve.</summary>
/// <remarks>
/// <para>
/// L'audit est le seul point du depot qui regarde plusieurs genres de fichiers
/// a la fois : les schemas, les profils et les packs. Il ne juge pas de leur
/// qualite artistique, seulement de leur conformite.
/// </para>
/// <para>
/// Ce qui est absent n'est pas signale. Un depot sans <c>packs/</c> n'a aucun
/// pack a auditer, et c'est un depot normal, pas un depot casse. En revanche,
/// un schema illisible, lui, est toujours signale.
/// </para>
/// </remarks>
public sealed class RepositoryAuditor
{
    /// <summary>Repertoires qui ne contiennent jamais de contenu a auditer.</summary>
    public static IReadOnlyList<string> PrunedDirectories { get; } = new[]
    {
        ".cache",
        ".codenomad",
        ".git",
        ".tmp-tests",
        ".venv",
        "TestResults",
        "bin",
        "build",
        "dist",
        "node_modules",
        "obj",
        "out",
    };

    private readonly IPackAudioProbe? _probe;

    /// <summary>Construit un auditeur.</summary>
    /// <param name="probe">
    /// Lecteur d'audio. Sans lui, la concordance de <c>audio.sha256</c> n'est
    /// pas verifiee — et le validateur le dit.
    /// </param>
    public RepositoryAuditor(IPackAudioProbe? probe = null)
    {
        _probe = probe;
    }

    /// <summary>Audite un depot.</summary>
    /// <param name="root">Racine du depot.</param>
    /// <param name="result">Constats globaux, ajoutes a la liste rendue.</param>
    /// <returns>Un sujet par fichier audite, dans l'ordre des genres.</returns>
    public IReadOnlyList<AuditSubject> Audit(string root, ValidationResult? result = null)
    {
        ArgumentNullException.ThrowIfNull(root);

        ValidationResult global = result ?? new ValidationResult();
        List<AuditSubject> subjects = new();

        if (!Directory.Exists(root))
        {
            global.Error(AuditCodes.AuditPathMissing, $"« {root} » n'est pas un repertoire.");
            return subjects;
        }

        AuditSchemas(root, global, subjects);
        AuditProfiles(root, global, subjects);
        AuditPacks(root, global, subjects);
        return subjects;
    }

    private static void AuditSchemas(string root, ValidationResult global, List<AuditSubject> subjects)
    {
        string directory = Path.Combine(root, "schema");
        if (!Directory.Exists(directory))
        {
            return;
        }

        ValidationResult subject = new();
        SchemaContract.CheckDirectory(directory, subject);
        subjects.Add(new AuditSubject("schema", "schema/", subject));
        _ = global;
    }

    private static void AuditProfiles(string root, ValidationResult global, List<AuditSubject> subjects)
    {
        string directory = Path.Combine(root, "profiles");
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (string file in SortedFiles(directory, "*.json"))
        {
            ValidationResult result = new();
            string expected = Path.GetFileNameWithoutExtension(file);
            string label = $"profiles/{expected}.json";

            string json;
            try
            {
                json = File.ReadAllText(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                result.Error(AuditCodes.ProfileUnreadable, $"{label} est illisible : {ex.Message}", label);
                subjects.Add(new AuditSubject("profile", label, result));
                continue;
            }

            if (ProfileValidator.TryRead(json, expected, result, out PlayerProfile? profile) && profile is not null)
            {
                result.Merge(ProfileValidator.Validate(profile, expected));
            }

            subjects.Add(new AuditSubject("profile", label, result));
        }

        _ = global;
    }

    private void AuditPacks(string root, ValidationResult global, List<AuditSubject> subjects)
    {
        Dictionary<string, IReadOnlyDictionary<string, PackExpectation>> expectations = new(StringComparer.Ordinal);

        foreach (string file in FindPacks(root))
        {
            string directory = Path.GetDirectoryName(file) ?? root;
            if (!expectations.TryGetValue(directory, out IReadOnlyDictionary<string, PackExpectation>? known))
            {
                known = LoadExpectations(directory, global);
                expectations[directory] = known;
            }

            string name = Path.GetFileName(file);
            string label = RelativeLabel(root, file);
            ValidationResult result = new();
            ValidationResult actual = new();
            LoadedPack? pack = null;

            try
            {
                pack = PackReader.Read(file);
            }
            catch (PackFormatException ex)
            {
                actual.Error(ValidationCodes.PackNotAnArchive, $"{label} : {ex.Message}", label);
            }
            catch (IOException ex)
            {
                actual.Error(ValidationCodes.PackNotAnArchive, $"{label} est illisible : {ex.Message}", label);
            }
            catch (UnauthorizedAccessException ex)
            {
                actual.Error(ValidationCodes.PackNotAnArchive, $"{label} est inaccessible : {ex.Message}", label);
            }

            if (pack is not null)
            {
                using (pack)
                {
                    actual.Merge(PackValidator.Validate(pack, _probe));
                }
            }

            if (known.TryGetValue(name, out PackExpectation? expectation))
            {
                // Un pack casse ici n'est pas un pack casse du depot : c'est un
                // test. Ce qui compte est qu'il casse comme on l'attendait.
                expectation.Check(label, actual, result);
                subjects.Add(new AuditSubject("pack", label, result) { Expected = true });
                continue;
            }

            if (known.Count > 0)
            {
                // Un repertoire indexe et un pack qui ne l'est pas : soit le
                // fichier a ete ajoute sans qu'on dise quoi qu'il soit, soit il
                // a ete retire de l'index en oubliant de le supprimer.
                result.Error(
                    AuditCodes.ExpectationMissing,
                    $"{label} n'est annonce par aucun {PackExpectation.IndexFileName}. "
                    + "Dans un repertoire indexe, tout pack se declare — meme valide.",
                    label);
            }

            result.Merge(actual);
            subjects.Add(new AuditSubject("pack", label, result));
        }
    }

    private static IReadOnlyDictionary<string, PackExpectation> LoadExpectations(
        string directory,
        ValidationResult global)
    {
        return PackExpectation.TryLoad(directory, out IReadOnlyDictionary<string, PackExpectation> map, global)
            ? map
            : new Dictionary<string, PackExpectation>(StringComparer.Ordinal);
    }

    /// <summary>Repertoire d'un chemin, avec la racine enlevée.</summary>
    public static string RelativeLabel(string root, string file)
    {
        string relative = Path.GetRelativePath(root, file);
        return relative.Replace(Path.DirectorySeparatorChar, '/');
    }

    private static string[] SortedFiles(string directory, string pattern)
    {
        string[] files = Directory.GetFiles(directory, pattern, SearchOption.TopDirectoryOnly);
        Array.Sort(files, StringComparer.Ordinal);
        return files;
    }

    private static IEnumerable<string> FindPacks(string root)
    {
        Stack<string> pending = new();
        pending.Push(root);

        while (pending.Count > 0)
        {
            string current = pending.Pop();

            foreach (string file in Directory.GetFiles(current, "*" + PackFormat.PackExtension, SearchOption.TopDirectoryOnly))
            {
                yield return file;
            }

            foreach (string child in Directory.GetDirectories(current, "*", SearchOption.TopDirectoryOnly))
            {
                string name = Path.GetFileName(child);
                if (PrunedDirectories.Contains(name, StringComparer.Ordinal))
                {
                    continue;
                }

                pending.Push(child);
            }
        }
    }
}
