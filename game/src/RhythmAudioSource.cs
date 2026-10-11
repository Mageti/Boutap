// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Boutap.Audio;
using Godot;

namespace Boutap.Shell;

/// <summary>
/// La position audible de la musique, dans la forme qu'attend
/// <see cref="AudioClock"/>.
/// </summary>
/// <remarks>
/// <para>
/// C'est la recette de la documentation Godot pour les jeux de rythme
/// (<c>tutorials/audio/sync_with_audio.rst</c>), et c'est la decision A0 :
/// <c>GetPlaybackPosition() + AudioServer.GetTimeSinceLastMix()
/// - AudioServer.GetOutputLatency()</c>. Les deux premiers termes donnent
/// l'instant du prochain bloc, le troisieme retire ce que la carte son n'a pas
/// encore fait entendre. Sans lui, tout le morceau est juge avec 15 ms de
/// retard, et l'ecran de resultat dirait « tu joues en retard » a un joueur qui
/// est juste ponctuel.
/// </para>
/// <para>
/// La latence de sortie est la seule quantite que l'hote impose : le reglage
/// <c>audio/driver/output_latency</c> de project.godot n'est qu'une demande. C7
/// se verifiera donc sur <see cref="MeasuredOutputLatencyMilliseconds"/> et pas
/// sur le reglage.
/// </para>
/// <para>
/// La documentation C# de Godot 4.3 previent que <c>GetOutputLatency()</c> est
/// couteux. On la lit donc une fois par trente images. Ce n'est pas une
/// approximation : l'hote lui-meme ne la fait bouger qu'a l'ouverture du
/// peripherique, jamais en cours de lecture.
/// </para>
/// </remarks>
public sealed class RhythmAudioSource : IAudioPositionSource
{
    /// <summary>Images entre deux lectures de la latence de sortie.</summary>
    private const int LatencyRefreshFrames = 30;

    private readonly AudioStreamPlayer _player;
    private double _frozenSeconds;
    private double _outputLatency;
    private int _sinceLatency = LatencyRefreshFrames;

    /// <summary>Construit la source sur un lecteur deja monte dans l'arbre.</summary>
    /// <param name="player">Le lecteur qui fait entendre la musique.</param>
    public RhythmAudioSource(AudioStreamPlayer player)
    {
        ArgumentNullException.ThrowIfNull(player);
        _player = player;
    }

    /// <summary>Latence de sortie reellement rapportee par l'hote, en millisecondes.</summary>
    public double MeasuredOutputLatencyMilliseconds => _outputLatency * 1000.0;

    /// <inheritdoc/>
    /// <remarks>
    /// Le taux de mixage du serveur audio, pas celui du fichier : la position
    /// rapportee par Godot est exprimee dans le temps du flux, et convertir
    /// secondes puisrames avec la MEme taux des deux cotes round-trippe
    /// exactement, quelle que soit la valeur.
    /// </remarks>
    public double SampleRate => AudioServer.GetMixRate();

    /// <inheritdoc/>
    public bool IsRunning => _player.Playing;

    /// <summary>
    /// Oublie la position retenue, pour une reprise depuis le debut.
    /// </summary>
    /// <remarks>
    /// Sans cet appel, la position figee resterait gagnee : on ne redescend
    /// jamais, donc relancer le morceau depuis zero le laisserait annonce la ou
    /// il s'etait arrete. C'est ce que fait la touche R, et un joueur qui
    /// rejoue doit retrouver ses notes au meme endroit.
    /// </remarks>
    public void Reset()
    {
        _frozenSeconds = 0;
        _sinceLatency = LatencyRefreshFrames;
    }

    /// <inheritdoc/>
    public long FramePosition
    {
        get
        {
            double seconds;
            if (!_player.Playing)
            {
                seconds = _frozenSeconds;
            }
            else
            {
                if (_sinceLatency >= LatencyRefreshFrames)
                {
                    _outputLatency = AudioServer.GetOutputLatency();
                    _sinceLatency = 0;
                }

                _sinceLatency++;

                // La documentation previent que la position peut jitter d'un
                // bloc a l'autre. Une horloge de jeu qui recule fait sauter le
                // jugement, donc on ne remonte jamais.
                double raw = _player.GetPlaybackPosition()
                    + AudioServer.GetTimeSinceLastMix()
                    - _outputLatency;
                seconds = raw < _frozenSeconds ? _frozenSeconds : raw;
                if (seconds < 0)
                {
                    seconds = 0;
                }

                _frozenSeconds = seconds;
            }

            return (long)Math.Round(seconds * SampleRate);
        }
    }
}
