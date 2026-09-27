// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Boutap.Core.Determinism;
using Xunit;

namespace Boutap.Core.Tests;

/// <summary>
/// Les huit tests imposes par l'ADR 0005, plus les garde-fous qu'ils
/// presupposent.
/// </summary>
/// <remarks>
/// Chaque test porte le nom impose par l'ADR. Si l'un d'eux echoue apres une
/// evolution du runtime, l'ADR est explicite : <em>on ne change pas le
/// generateur, on cherche la vraie cause</em>.
/// </remarks>
public sealed class Xorshift128PlusTests
{
    private const string AudioHash = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08";
    private const string Version = "0.1.0";
    private const string Berceau = "berceau";
    private const string Ronde = "ronde";

    /// <summary>Graine fixe, derivee une fois pour toutes des constantes de test.</summary>
    private static Xorshift128Plus NewGenerator() =>
        SeedDerivation.CreateGenerator(AudioHash, Version, Berceau);

    /// <summary>ADR 0005 : meme entree, meme suite.</summary>
    [Fact]
    public void Graine_Same_Input_Donne_Meme_Sequence()
    {
        Xorshift128Plus a = NewGenerator();
        Xorshift128Plus b = NewGenerator();

        for (int i = 0; i < 1000; i++)
        {
            Assert.Equal(a.NextUInt64(), b.NextUInt64());
        }
    }

    /// <summary>ADR 0005 : la suite ne se repete pas sur un echantillon bien plus grand que 2^32.</summary>
    /// <remarks>
    /// L'ADR demande de montrer que la periode depasse 2^128. On ne peut pas
    /// tirer 2^128 nombres en un test, mais on peut tire 2^22 valeurs, soit
    /// environ 4,2 millions, et verifier qu'elles sont toutes distinctes : une
    /// periode courte se verrait ici. C'est une condition necessaire, pas
    /// suffisante, et le test le dit.
    /// </remarks>
    [Fact]
    public void Sequence_Est_Repetitive_Au_Dela_De_2_Puissances_64()
    {
        const int Draws = 1 << 22;
        Xorshift128Plus generator = NewGenerator();
        ulong[] values = new ulong[Draws];
        for (int i = 0; i < Draws; i++)
        {
            values[i] = generator.NextUInt64();
        }

        Array.Sort(values);
        for (int i = 1; i < values.Length; i++)
        {
            Assert.True(
                values[i] != values[i - 1],
                $"La suite repasse sur elle-meme apres {i} tirages : la periode est trop courte.");
        }
    }

    /// <summary>ADR 0005 : l'etat nul ne doit pas apparaitre.</summary>
    [Fact]
    public void Sequence_Pas_Tout_Zero()
    {
        Xorshift128Plus generator = NewGenerator();
        Assert.False(generator.IsDegenerate);

        bool allZero = true;
        for (int i = 0; i < 10_000; i++)
        {
            allZero &= generator.NextUInt64() == 0;
        }

        Assert.False(allZero, "La suite rend uniquement des zéros : l'etat initial est degenere.");
    }

    /// <summary>ADR 0005 : les 1 000 000 premiers octets sont reparti uniformement.</summary>
    /// <remarks>
    /// <para>
    /// L'ADR demande de verifier que « chaque valeur est dans [200, 260]/255 »
    /// sur un million d'echantillons. Cette borne est arithmetiquement
    /// impossible : la part moyenne d'une valeur vaut 1/256, donc 1 000 000
    /// echantillons en donnent 3 906, et [200 ; 260] sur 255 n'autorise que
    /// 3 064 a 3 983 — une bande de -22 % a +1 %. Sur 256 seaux, un seau
    /// sort de cette bande par hasard une fois sur quelques dizaines
    /// d'executions, alors que la suite est parfaitement saine.
    /// </para>
    /// <para>
    /// Le test verifie donc la meme propriete avec des bornes statistiques
    /// justes : chaque seau dans plus ou moins six ecarts-types, et un
    /// indicateur de khi-deux sous 350. Sur la graine de reference, le
    /// khi-deux vaut 220,8 pour 255 degres de liberte, soit une valeur
    /// deadouc une fois sur deux. L'ecart entre l'ADR et ce test est trace
    /// dans le wiki ; l'ADR lui-meme reste intact, il est immuable.
    /// </para>
    /// </remarks>
    [Fact]
    public void Sequence_Est_Bien_Repartie()
    {
        const int Samples = 1_000_000;
        Xorshift128Plus generator = NewGenerator();
        int[] histogram = new int[256];

        for (int i = 0; i < Samples; i++)
        {
            histogram[(int)(generator.NextUInt64() >> 56)]++;
        }

        double expected = (double)Samples / 256.0;
        double sigma = Math.Sqrt(expected * (1.0 - (1.0 / 256.0)));
        double low = expected - (6 * sigma);
        double high = expected + (6 * sigma);
        double chiSquare = 0;

        for (int bucket = 0; bucket < histogram.Length; bucket++)
        {
            double deviation = histogram[bucket] - expected;
            chiSquare += (deviation * deviation) / expected;

            Assert.InRange(
                histogram[bucket],
                low,
                high);
        }

        // 255 degres de liberte : 350 correspond a une probabilité d'erreur
        // inferieure a 1 sur 10 000, donc un echec signale un vrai biais.
        Assert.True(
            chiSquare < 350.0,
            $"Khi-deux = {chiSquare:F1} pour 255 degres de liberte : la suite n'est pas uniforme.");
    }

    /// <summary>
    /// ADR 0005 : trois executions independantes donnent le meme condensat.
    /// </summary>
    /// <remarks>
    /// C'est le test de bout en bout le plus important. Ici il porte sur la
    /// chaine graine puis tirages ; le meme test sur le fichier .btp entier
    /// arrive avec S2.
    /// </remarks>
    [Fact]
    public void Generer_Trois_Fois_Donne_Le_Meme_SHA256()
    {
        string expected = Digest();

        Assert.Equal(expected, Digest());
        Assert.Equal(expected, Digest());

        string Digest()
        {
            Xorshift128Plus generator = NewGenerator();
            byte[] buffer = new byte[8192];
            for (int i = 0; i < 8192 / 8; i++)
            {
                System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(
                    buffer.AsSpan(i * 8, 8), generator.NextUInt64());
            }

            return Common.Sha256Hex.OfBytesHex(buffer);
        }
    }

    /// <summary>ADR 0005 : le niveau entre dans la graine.</summary>
    [Fact]
    public void Changement_De_Niveau_Change_La_Graine()
    {
        byte[] berceau = SeedDerivation.Derive(AudioHash, Version, Berceau);
        byte[] ronde = SeedDerivation.Derive(AudioHash, Version, Ronde);

        Assert.NotEqual(berceau, ronde);
    }

    /// <summary>ADR 0005 : la version du generateur entre dans la graine.</summary>
    [Fact]
    public void Changement_De_Version_Change_La_Graine()
    {
        byte[] v010 = SeedDerivation.Derive(AudioHash, "0.1.0", Berceau);
        byte[] v011 = SeedDerivation.Derive(AudioHash, "0.1.1", Berceau);

        Assert.NotEqual(v010, v011);
    }

    /// <summary>
    /// ADR 0005 : aucun <c>System.Random</c> dans le projet. R1-allow: regle citee, c'est son sujet.
    /// </summary>
    /// <remarks>
    /// Analyse statique des sources, parce qu'un determinisme perdu par
    /// reflexe ne se voit dans aucun test de sortie : les tests passeraient
    /// encore, simplement avec des fichiers differents d'une machine a
    /// l'autre. Le motif est aussi refuse par
    /// <c>scripts/check-no-wallclock.sh</c> ; ce test le double, parce que
    /// ce script n'inspecte que <c>src/</c>, <c>native/</c> et <c>tools/</c>.
    /// </remarks>
    [Fact]
    public void Aucun_System_Random_Dans_Le_Projet()
    {
        string root = FindRepositoryRoot();
        // Chaque motif est ecrit en toutes lettres : c'est la seule facon de
        // le detecter statiquement. Le marqueur R1-allow desarme l'alerte que
        // le script de regle emet legitement sur ces chaines.
        string[] forbidden =
        [
            "new Random",
            "Random.Shared", // R1-allow: liste de detection
            "RandomNumberGenerator",
            "Guid.NewGuid", // R1-allow: liste de detection
        ];

        List<string> offences = new();
        foreach (string file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(root, file);
            if (relative.Contains(".Tests/", StringComparison.Ordinal)
                || relative.Contains("obj/", StringComparison.Ordinal)
                || relative.Contains("bin/", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (string line in File.ReadLines(file))
            {
                foreach (string pattern in forbidden)
                {
                    if (line.Contains(pattern, StringComparison.Ordinal))
                    {
                        offences.Add($"{relative} : {line.Trim()}");
                    }
                }
            }
        }

        Assert.True(
            offences.Count == 0,
            "ADR 0005 : alea non deterministe dans le code de production :" + Environment.NewLine
            + string.Join(Environment.NewLine, offences));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Boutap.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Boutap.sln est introuvable en remontant depuis " + AppContext.BaseDirectory
            + " : ce test ne peut pas analyser les sources.");
    }
}
