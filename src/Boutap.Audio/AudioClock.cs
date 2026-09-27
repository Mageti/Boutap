// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Diagnostics;

namespace Boutap.Audio;

/// <summary>
/// Horloge de jeu, derivee de la position de lecture audio.
/// </summary>
/// <remarks>
/// <para>
/// C'est l'unique source de temps du projet (wiki: spec.md §7.3 et §9). Tout
/// ce qui doit etre reproductible — jugement d'une note, curve de charge,
/// animation — se lit ici, et nulle part ailleurs.
/// </para>
/// <para>
/// Une carte son ne signale sa position que par blocs : typiquement toutes les
/// 512 echantillons, soit environ 10 ms. Lire cette position telle quelle donne
/// une courbe en escalier, et un escalier dans le temps de jeu se voit et
/// s'entend. On interpole donc entre deux rapports, et on resynchronise a
/// chaque rapport pour que l'erreur d'extrapolation ne s'accumule pas.
/// </para>
/// <para>
/// Aucune horloge murale n'est lue ici. <see cref="Stopwatch"/> sert a mesurer
/// une duree ecoulee — sur Windows, il s'appuie sur
/// <c>QueryPerformanceCounter</c>, d'ou l'autorisation portee par
/// <c>scripts/check-no-wallclock.sh</c> pour ce fichier.
/// </para>
/// </remarks>
public sealed class AudioClock
{
    /// <summary>Frequence d'echantillonnage par defaut, en Hz.</summary>
    public const double DefaultSampleRate = 48000.0;

    /// <summary>Taille de bloc audio courante, en echantillons.</summary>
    public const int DefaultBlockFrames = 512;

    private readonly IAudioPositionSource _source;
    private long _anchorFrame;
    private long _anchorTimestamp;
    private double _anchorSeconds;
    private double _rateCorrection = 1.0;
    private double _userOffsetSeconds;

    /// <summary>Construit une horloge sur une source de lecture.</summary>
    /// <param name="source">Source qui fournit la position, en echantillons.</param>
    /// <param name="blockFrames">Taille de bloc annoncee par la carte son.</param>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> est nul.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="blockFrames"/> n'est pas strictement positif.
    /// </exception>
    public AudioClock(IAudioPositionSource source, int blockFrames = DefaultBlockFrames)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfLessThan(blockFrames, 1);

        _source = source;
        BlockFrames = blockFrames;
        _anchorFrame = source.FramePosition;
        _anchorSeconds = _anchorFrame / SampleRate;
        _anchorTimestamp = Stopwatch.GetTimestamp();
    }

    /// <summary>Taille de bloc annoncee par la carte son, en echantillons.</summary>
    public int BlockFrames { get; }

    /// <summary>Frequence d'echantillonnage de la source, en Hz.</summary>
    public double SampleRate
    {
        get
        {
            double rate = _source.SampleRate;
            return rate > 0 ? rate : DefaultSampleRate;
        }
    }

    /// <summary>
    /// Decalage applique par l'utilisateur, en secondes, positif pour retarder
    /// le jugement.
    /// </summary>
    /// <remarks>
    /// Eleve depuis les reglages d'accessibilite, jamais depuis l'horloge
    /// murale. Une valeur de 0,08 signifie « la note est jugee 80 ms plus
    /// tot que le son que j'entends », ce qui est le cas typique d'un ecran
    /// tactile et d'un casque filaire.
    /// </remarks>
    public double UserOffsetSeconds
    {
        get => _userOffsetSeconds;
        set
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value), value, "Le decalage utilisateur doit etre un nombre fini.");
            }

            if (value < -1.0 || value > 1.0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value), value, "Le decalage utilisateur doit tenir dans [-1 s ; +1 s].");
            }

            _userOffsetSeconds = value;
        }
    }

    /// <summary>
    /// Correction de derive mesuree, en echantillons de moteur par echantillon
    /// de carte son.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Vaut 1,0 tant que la derive n'a pas ete mesuree. La mesure elle-meme
    /// appartient a S1 (<c>boutap bench latency</c>) : ici on ne fait que
    /// appliquer un nombre mesure ailleurs, ce qui garde l'horloge de jeu
    /// independante du materiel et donc deterministe.
    /// </para>
    /// <para>
    /// La derive typique d'un crystal audio est de l'ordre de 100 ppm, soit
    /// 0,3 ms par minute. La corriger n'est pas indispensable sur un morceau
    /// de trois minutes, mais elle devient visible sur un maree de dix
    /// minutes ou en concatenation gapless.
    /// </para>
    /// </remarks>
    public double RateCorrection
    {
        get => _rateCorrection;
        set
        {
            if (double.IsNaN(value) || value <= 0 || value > 1.1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value), value, "La correction de derive doit tenir dans ]0 ; 1,1].");
            }

            _rateCorrection = value;
        }
    }

    /// <summary>Position de lecture, en secondes, sans le decalage utilisateur.</summary>
    public double PositionSeconds
    {
        get
        {
            double sinceAnchor = (Stopwatch.GetTimestamp() - _anchorTimestamp)
                / (double)Stopwatch.Frequency;

            // On n'extrapole jamais au-dela d'un bloc : au-dela, la position
            // rapportee par la carte son serait plus fiable que notre
            // extrapolation, et l'on prefererait une micro-hesitation a une
            // erreur de plusieurs dizaines de millisecondes.
            double cap = BlockFrames / SampleRate;
            if (sinceAnchor < 0)
            {
                sinceAnchor = 0;
            }
            else if (sinceAnchor > cap)
            {
                sinceAnchor = cap;
            }

            return _anchorSeconds + (sinceAnchor * _rateCorrection);
        }
    }

    /// <summary>Position de lecture, en secondes, decalee par les reglages.</summary>
    public double JudgementTimeSeconds => PositionSeconds + _userOffsetSeconds;

    /// <summary>Position de lecture, en echantillons, sans interpolation.</summary>
    public long FramePosition => _source.FramePosition;

    /// <summary>
    /// Compteur monotone au dernier resynchronisme, en unites de
    /// <c>Stopwatch.Frequency</c>.
    /// </summary>
    /// <remarks>
    /// C'est l'ancre qui relie l'horloge du systeme a l'horloge audio. Un
    /// evenement d'entree, horodate dans le meme systeme, se ramene donc au
    /// temps audio par une simple soustraction. Voir <c>Boutap.Input.InputClock</c>.
    /// </remarks>
    public long TimestampAtAnchor => _anchorTimestamp;

    /// <summary>Position de lecture au dernier resynchronisme, en secondes.</summary>
    public double PositionSecondsAtAnchor => _anchorSeconds;

    /// <summary>
    /// Resynchronise l'interpolation sur la position reellement rapportee.
    /// </summary>
    /// <remarks>
    /// A appeler a chaque rapport de la carte son, donc environ toutes les
    /// 10 ms. Sans cet appel, l'extrapolation continuerait de diverger du
    /// materiel pour une raison qui n'a rien a voir avec la montre du joueur.
    /// </remarks>
    public void Resync()
    {
        _anchorFrame = _source.FramePosition;
        _anchorSeconds = _anchorFrame / SampleRate;
        _anchorTimestamp = Stopwatch.GetTimestamp();
    }

    /// <summary>Convertit une position en secondes vers un numero d'echantillon.</summary>
    /// <param name="seconds">Position en secondes.</param>
    /// <returns>Le numero d'echantillon correspondant, arrondi.</returns>
    public long SecondsToFrame(double seconds) => (long)Math.Round(seconds * SampleRate);

    /// <summary>Convertit un numero d'echantillon vers une position en secondes.</summary>
    /// <param name="frame">Numero d'echantillon.</param>
    /// <returns>La position correspondante, en secondes.</returns>
    public double FrameToSeconds(long frame) => frame / SampleRate;
}
