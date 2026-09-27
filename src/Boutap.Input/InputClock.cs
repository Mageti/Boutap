// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Diagnostics;
using Boutap.Audio;

namespace Boutap.Input;

/// <summary>
/// Convertit l'horodatage d'un evenement d'entree en temps de jeu.
/// </summary>
/// <remarks>
/// <para>
/// Un evenement d'entere porte l'heure a laquelle le joueur a agi ; le moteur
/// de jeu raisonne en temps audio. Ce type fait la conversion, et surtout
/// tranquille : il ne modifie jamais l'ordre des evenements, ce qui doit rester
/// l'ordre d'arrivee de la couche materiel.
/// </para>
/// <para>
/// La latence d'entree est une propriete de la machine du joueur — pilote,
/// port USB, compositeur — et se mesure une fois au demarrage, jamais en
/// boucle de jeu. <see cref="MeasureLatencySeconds"/> fait cette mesure, hors
/// partie, pour le resultat de S1 (<c>boutap bench latency</c>). Sur Windows,
/// <c>Stopwatch.GetTimestamp</c> s'appuie sur <c>QueryPerformanceCounter</c>,
/// d'ou l'autorisation portee par <c>scripts/check-no-wallclock.sh</c> pour ce
/// fichier.
/// </para>
/// </remarks>
public sealed class InputClock
{
    private double _latencySeconds;

    /// <summary>Construit un convertisseur sur une horloge de jeu.</summary>
    /// <param name="audioClock">Horloge de jeu servant de reference.</param>
    /// <exception cref="ArgumentNullException"><paramref name="audioClock"/> est nul.</exception>
    public InputClock(AudioClock audioClock)
    {
        ArgumentNullException.ThrowIfNull(audioClock);
        AudioClock = audioClock;
    }

    /// <summary>Horloge de jeu de reference.</summary>
    public AudioClock AudioClock { get; }

    /// <summary>
    /// Latence d'entree retenue, en secondes, positive si l'entree arrive en
    /// retard sur le son.
    /// </summary>
    /// <remarks>
    /// Estimée une fois au demarrage, puis figee. La recalculer en boucle
    /// ferait respirer le jugement du joueur, ce qui est le pire defaut
    /// possible pour un jeu de rythme.
    /// </remarks>
    public double LatencySeconds
    {
        get => _latencySeconds;
        set
        {
            if (double.IsNaN(value) || value < 0 || value > 0.5)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value), value, "La latence doit tenir dans [0 s ; 500 ms].");
            }

            _latencySeconds = value;
        }
    }

    /// <summary>Lit le compteur monotone courant, en unites de <c>Stopwatch.Frequency</c>.</summary>
    /// <returns>La valeur a copier dans un <see cref="InputEvent"/>.</returns>
    public static long Now() => Stopwatch.GetTimestamp();

    /// <summary>Convertit un horodatage d'evenement en temps de jeu.</summary>
    /// <param name="timestamp">Horodatage de l'evenement.</param>
    /// <returns>
    /// Le temps de jeu correspondant, en secondes, decale de la latence
    /// d'entree retenue.
    /// </returns>
    public double ToGameTime(long timestamp) => ToGameTime(timestamp, 0);

    /// <summary>Convertit un horodatage d'evenement en temps de jeu.</summary>
    /// <param name="timestamp">Horodatage de l'evenement.</param>
    /// <param name="extraLatencySeconds">
    /// Latence supplementaire a retrancher, propre a l'appelant.
    /// </param>
    /// <returns>Le temps de jeu correspondant, en secondes.</returns>
    public double ToGameTime(long timestamp, double extraLatencySeconds)
    {
        double elapsedSeconds = (timestamp - AudioClock.TimestampAtAnchor)
            / (double)Stopwatch.Frequency;

        return AudioClock.PositionSecondsAtAnchor
            + (elapsedSeconds * AudioClock.RateCorrection)
            - _latencySeconds
            - extraLatencySeconds;
    }

    /// <summary>Convertit un temps de jeu en echantillon audio.</summary>
    /// <param name="gameTimeSeconds">Temps de jeu, en secondes.</param>
    /// <returns>La position audio correspondante, en echantillons.</returns>
    public long ToAudioFrame(double gameTimeSeconds) => AudioClock.SecondsToFrame(gameTimeSeconds);

    /// <summary>
    /// Mesure la latence d'entree en rejouant un nombre d'evenements connus.
    /// </summary>
    /// <param name="samples">
    /// Ecarts, en unites de <c>Stopwatch.Frequency</c>, mesures entre l emitting
    /// d'un evenement et sa reception par la boucle de jeu.
    /// </param>
    /// <returns>La latence mediane, en secondes.</returns>
    /// <remarks>
    /// La mediane et non la moyenne : une moyenne est tiree vers le haut par le
    /// premier evenement, celui qui paie le chargement de la page, et c'est
    /// precisement celle-la qu'on ne veut pas voir dans la note affichee au
    /// joueur. Voir wiki: spec.md §9.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="samples"/> est vide.</exception>
    public static double MeasureLatencySeconds(ReadOnlySpan<long> samples)
    {
        if (samples.Length == 0)
        {
            throw new ArgumentException(
                "Aucun echantillon : la latence d'entree ne peut pas etre mesuree.", nameof(samples));
        }

        long[] sorted = samples.ToArray();
        Array.Sort(sorted);
        long median = sorted[sorted.Length / 2];

        return median / (double)Stopwatch.Frequency;
    }
}
