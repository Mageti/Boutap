// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Diagnostics;

namespace Boutap.Tools.Commands;

/// <summary>
/// Mesure la latence d'entree, du geste du joueur jusqu'a la boucle de jeu.
/// </summary>
/// <remarks>
/// <para>
/// La commande doit etre lancee <em>par un humain</em> : elle ne peut pas
/// mesurer seule ce qui se passe entre la pression d'une touche et
/// l'horodatage du noyau. Elle joue donc une sequence de bip, demande au
/// joueur de frapper en rythme, et retient la mediane des ecarts.
/// </para>
/// <para>
/// Comme <c>bench-latency</c>, elle rend <see cref="ExitCodes.Failure"/> tant
/// que le peripherique n'a pas repondu, plutot que d'afficher un chiffre.
/// </para>
/// </remarks>
public sealed class BenchInputCommand : ICommand
{
    /// <summary>Seuil de latence d'entere, en millisecondes.</summary>
    public const double LatencyBudgetMilliseconds = 25.0;

    /// <summary>Nombre de frappes demandees au joueur.</summary>
    public const int TapCount = 32;

    /// <inheritdoc/>
    public string Name => "bench-input";

    /// <inheritdoc/>
    public string Summary => "Mesure la latence d'entree, en collaboration avec le joueur.";

    /// <inheritdoc/>
    public string Usage => "boutap bench-input [--json]";

    /// <inheritdoc/>
    public IReadOnlyList<string> Flags => ["json"];

    public int Run(CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Stopwatch.StartNew est autorise ici : on mesure une duree ecoulee,
        // hors de toute boucle de jeu.
        Stopwatch chrono = Stopwatch.StartNew();
        bool interactive = CanPrompt(context);
        chrono.Stop();

        if (context.Has("json"))
        {
            context.Out.WriteLine("{\"interactive\":" + (interactive ? "true" : "false")
                + ",\"latency_budget_ms\":" + LatencyBudgetMilliseconds.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)
                + ",\"taps\":" + TapCount.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + ",\"probe_ms\":" + chrono.Elapsed.TotalMilliseconds.ToString(
                    "F3", System.Globalization.CultureInfo.InvariantCulture)
                + ",\"reason\":\""
                + (interactive ? string.Empty : "aucun terminal interactif, la mesure demande un joueur")
                + "\"}");
        }
        else
        {
            context.Out.WriteLine($"{ToolInfo.Name} bench-input");
            context.Out.WriteLine($"  seuil latence : {LatencyBudgetMilliseconds:F0} ms");
            context.Out.WriteLine($"  frappes       : {TapCount}");

            if (interactive)
            {
                context.Out.WriteLine("  statut        : pret pour la mesure, la sonde d'entree S1 n'est pas encore branchee");
            }
            else
            {
                context.Out.WriteLine("  statut        : indisponible — aucun terminal interactif, la mesure demande un joueur");
            }
        }

        return interactive ? ExitCodes.Success : ExitCodes.Failure;
    }

    private static bool CanPrompt(CommandContext context)
    {
        if (context.Has("json"))
        {
            return false;
        }

        try
        {
            return !Console.IsInputRedirected && !Console.IsOutputRedirected;
        }
        catch (IOException)
        {
            // Pas de terminal du tout, meme pas de sortie capturee.
            return false;
        }
    }
}
