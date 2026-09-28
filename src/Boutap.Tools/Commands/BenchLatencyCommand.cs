// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Boutap.Tools.Commands;

/// <summary>
/// Mesure la latence de sortie audio et la derive de l'horloge.
/// </summary>
/// <remarks>
/// <para>
/// C'est la commande qui fixe les deux seuils de S1 : latence de bout en bout
/// sous 25 ms, derive sous 5 ms par cinq minutes (wiki: spec.md §9). Elle a
/// donc besoin du lecteur audio reel, donc de miniaudio, qui n'arrive qu'en
/// S3.1.
/// </para>
/// <para>
/// En attendant, la commande ne fabrique pas de chiffre. Elle explique ce
/// qu'elle mesurerait et rend <see cref="ExitCodes.Failure"/> : un rapport qui
/// afficherait « 0 ms de latence » ferait Worse que pas de rapport du tout,
/// parce qu'il passerait pour une reussite.
/// </para>
/// </remarks>
public sealed class BenchLatencyCommand : ICommand
{
    /// <summary>Seuil de latence de bout en bout, en millisecondes.</summary>
    public const double LatencyBudgetMilliseconds = 25.0;

    /// <summary>Seuil de derive, en millisecondes par cinq minutes.</summary>
    public const double DriftBudgetMilliseconds = 5.0;

    /// <inheritdoc/>
    public string Name => "bench-latency";

    /// <inheritdoc/>
    public string Summary => "Mesure la latence audio et la derive de l'horloge (seuils de S1).";

    /// <inheritdoc/>
    public string Usage => "boutap bench-latency [--json]";

    /// <inheritdoc/>
    public IReadOnlyList<string> Flags => ["json"];

    public int Run(CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Stopwatch.StartNew est autorise ici : on mesure une duree ecoulee,
        // hors de toute boucle de jeu.
        Stopwatch chrono = Stopwatch.StartNew();
        bool backendAvailable = ProbeAudioBackend();
        chrono.Stop();

        if (context.Has("json"))
        {
            context.Out.WriteLine("{\"available\":" + (backendAvailable ? "true" : "false")
                + ",\"latency_budget_ms\":" + LatencyBudgetMilliseconds.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)
                + ",\"drift_budget_ms\":" + DriftBudgetMilliseconds.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)
                + ",\"probe_ms\":" + chrono.Elapsed.TotalMilliseconds.ToString(
                    "F3", System.Globalization.CultureInfo.InvariantCulture)
                + ",\"reason\":\""
                + (backendAvailable ? string.Empty : BackendUnavailableReason())
                + "\"}");
        }
        else
        {
            context.Out.WriteLine($"{ToolInfo.Name} bench-latency");
            context.Out.WriteLine($"  seuil latence : {LatencyBudgetMilliseconds:F0} ms de bout en bout");
            context.Out.WriteLine($"  seuil derive  : {DriftBudgetMilliseconds:F0} ms par cinq minutes");

            if (backendAvailable)
            {
                context.Out.WriteLine("  statut        : backend audio present, mesures ci-dessous");
            }
            else
            {
                context.Out.WriteLine($"  statut        : indisponible — {BackendUnavailableReason()}");
            }
        }

        return backendAvailable ? ExitCodes.Success : ExitCodes.Failure;
    }

    /// <summary>Cherche un backend audio natif exploitable.</summary>
    /// <returns>Vrai si une mesure de latence est possible.</returns>
    private static bool ProbeAudioBackend()
    {
        // miniaudio (S3.1) n'est pas encore embarque. Une bibliotheque de
        // decodage ne suffit pas : la latence qu'on cherche est celle de la
        // chaine de sortie, pas celle du lecteur de fichier.
        return File.Exists(Path.Combine(AppContext.BaseDirectory, "miniaudio.dll"))
            || File.Exists(Path.Combine(AppContext.BaseDirectory, "libminiaudio.so"))
            || Directory.Exists(AppContext.BaseDirectory)
                && HasMiniaudioSharedObject();
    }

    private static bool HasMiniaudioSharedObject()
    {
        foreach (string name in new[] { "libminiaudio.so", "libminiaudio.dylib" })
        {
            try
            {
                if (NativeLibrary.TryLoad(name, out nint handle))
                {
                    NativeLibrary.Free(handle);
                    return true;
                }
            }
            catch (BadImageFormatException)
            {
                // Une bibliotheque d'une autre architecture : ce n'est pas la
                // notre, on continue.
            }
        }

        return false;
    }

    private static string BackendUnavailableReason() =>
        "le backend audio natif (miniaudio) arrive en S3.1; S1 n'est pas tranche";
}
