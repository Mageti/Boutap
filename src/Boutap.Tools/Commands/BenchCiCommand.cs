// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Diagnostics;
using Boutap.Core.Determinism;
using Boutap.Core.Pack;

namespace Boutap.Tools.Commands;

/// <summary>
/// Mesure la dispersion du temps de calcul sur une charge fixe.
/// </summary>
/// <remarks>
/// <para>
/// Sert a distinguer « la CI est lente » de « la CI est instable ». La charge
/// est identique a chaque execution — memes graines, meme volume, memes
/// entrees — donc toute dispersion vient de la machine, jamais du code.
/// </para>
/// <para>
/// On rapporte la mediane et les extremes, pas seulement la moyenne : une
/// moyenne de dix executions/cachee une machine qui rame une fois sur dix.
/// </para>
/// </remarks>
public sealed class BenchCiCommand : ICommand
{
    private const int DefaultIterations = 10;
    private const int NoteCount = 40_000;
    private const int HashRounds = 24;

    /// <summary>Nom de niveau utilise pour la graine de la charge.</summary>
    private const string LevelName = "berceau";

    /// <inheritdoc/>
    public string Name => "bench-ci";

    /// <inheritdoc/>
    public string Summary => "Mesure la dispersion du temps de calcul sur une charge fixe.";

    /// <inheritdoc/>
    public string Usage => "boutap bench-ci [--iterations N] [--json]";

    /// <inheritdoc/>
    public int Run(CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        int iterations = DefaultIterations;
        if (context.TryGetValue("iterations", out string? raw))
        {
            if (!CommandLine.TryParseInt32(raw, out iterations) || iterations < 1 || iterations > 1000)
            {
                context.Warn($"--iterations attend un entier entre 1 et 1000, pas « {raw} ».");
                return ExitCodes.UsageError;
            }
        }

        double[] samples = new double[iterations];
        long workload = 0;

        for (int i = 0; i < iterations; i++)
        {
            // Stopwatch.StartNew est ici legitime : on mesure une duree ecoulee
            // hors de toute boucle de jeu, et rien dans le resultat ne depend
            // de la mesure.
            Stopwatch chrono = Stopwatch.StartNew();
            workload += RunWorkload((ulong)(i + 1));
            chrono.Stop();
            samples[i] = chrono.Elapsed.TotalMilliseconds;
        }

        Array.Sort(samples);
        double median = samples[samples.Length / 2];
        double worst = samples[^1];
        double best = samples[0];
        double spread = worst - best;

        if (context.Has("json"))
        {
            context.Out.WriteLine("{\"iterations\":" + iterations
                + ",\"workload\":" + workload.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + ",\"best_ms\":" + best.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)
                + ",\"median_ms\":" + median.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)
                + ",\"worst_ms\":" + worst.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)
                + ",\"spread_ms\":" + spread.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)
                + "}");
        }
        else
        {
            context.Out.WriteLine($"{ToolInfo.Name} bench-ci — {iterations} execution(s)");
            context.Out.WriteLine($"  charge      : {workload} unites");
            context.Out.WriteLine($"  mediane     : {median:F3} ms");
            context.Out.WriteLine($"  meilleur    : {best:F3} ms");
            context.Out.WriteLine($"  pire        : {worst:F3} ms");
            context.Out.WriteLine($"  dispersion  : {spread:F3} ms");

            // Un seuil, pas une verite : 50 ms d'ecart entre la meilleure et la
            // pire execution indique une machine qui throttle ou un runner
            // charge. Au-dela, la mesure ne sert plus a comparer deux versions.
            if (iterations >= 5 && spread > 50.0)
            {
                context.Out.WriteLine();
                context.Out.WriteLine("  Verdict     : instable — la dispersion domine la mesure.");
            }
        }

        return ExitCodes.Success;
    }

    /// <summary>Une charge fixe : un chart synthetise puis hache en boucle.</summary>
    /// <param name="seed">Graine de la synthese.</param>
    /// <returns>Une somme de controle, qui change des que la charge change.</returns>
    private static long RunWorkload(ulong seed)
    {
        // Meme graine a chaque execution : la charge est identique, donc la
        // dispersion mesuree vient de la machine et pas du code.
        Xorshift128Plus generator = SeedDerivation.CreateGenerator(
            new string('0', 64), "bench-ci/1", BenchCiCommand.LevelName);

        KeyBinding[] keys = new KeyBinding[KeyBinding.GridSize + 2];
        for (int i = 0; i < KeyBinding.GridSize; i++)
        {
            keys[i] = KeyBinding.Grid(i);
        }

        keys[KeyBinding.GridSize] = KeyBinding.WheelLeft;
        keys[KeyBinding.GridSize + 1] = KeyBinding.WheelRight;

        ulong accumulator = seed;
        for (int i = 0; i < NoteCount; i++)
        {
            KeyBinding key = keys[generator.NextInt32(0, keys.Length)];
            accumulator += (ulong)System.Text.Encoding.ASCII.GetByteCount(key.ToString());
            accumulator += (ulong)generator.NextDouble(0.0, 0.125);
        }

        Span<byte> block = stackalloc byte[32];
        for (int round = 0; round < HashRounds; round++)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(block, accumulator);
            byte[] digest = System.Security.Cryptography.SHA256.HashData(block);
            accumulator ^= System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(digest);
            accumulator = System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(accumulator);
        }

        return (long)(accumulator & 0x7FFFFFFF);
    }
}
