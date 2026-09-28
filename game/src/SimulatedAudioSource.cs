// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Boutap.Audio;

namespace Boutap.Shell;

/// <summary>
/// Une source de position audio qui n'en est pas une.
/// </summary>
/// <remarks>
/// <para>
/// Le vrai backend est miniaudio, qui arrive en S3.1 (wiki: spec.md 16). En
/// attendant, la coque a besoin de <em>quelque chose</em> qui respecte
/// <see cref="IAudioPositionSource"/> pour que <see cref="AudioClock"/> soit
/// exerce de bout en bout.
/// </para>
/// <para>
/// Cette classe rend donc une position synthetique avancee par le compteur
/// d'images de Godot, et elle le dit dans son nom. Elle ne pretendra jamais
/// etre un chronometre : le jour ou miniaudio branchera le vrai
/// <c>IAudioPositionSource</c>, cette classe disparait.
/// </para>
/// <para>
/// R1 : rien ici ne lit l'horloge murale. L'avancement vient du delta d'image
/// que le moteur fournit, qui est une information de rendu, pas une horloge.
/// </para>
/// </remarks>
public sealed class SimulatedAudioSource : IAudioPositionSource
{
    private long _framePosition;

    /// <summary>Cree une source a 48 kHz, convention du projet.</summary>
    public SimulatedAudioSource()
        : this(DefaultSampleRate)
    {
    }

    /// <summary>Cree une source a la frequence demandee.</summary>
    /// <param name="sampleRate">Frequence d'echantillonnage en Hz.</param>
    public SimulatedAudioSource(double sampleRate) => SampleRate = sampleRate;

    /// <summary>Frequence d'echantillonnage du projet.</summary>
    public const double DefaultSampleRate = 48000.0;

    /// <inheritdoc/>
    public double SampleRate { get; }

    /// <inheritdoc/>
    public long FramePosition => _framePosition;

    /// <inheritdoc/>
    public bool IsRunning { get; private set; }

    /// <summary>Demarre la simulation.</summary>
    public void Start() => IsRunning = true;

    /// <summary>Arrete la simulation : la position se fige.</summary>
    public void Stop() => IsRunning = false;

    /// <summary>Avance la position synthetique.</summary>
    /// <param name="deltaSeconds">Duree de l'image, fournie par le moteur.</param>
    public void Advance(double deltaSeconds)
    {
        if (!IsRunning || !(deltaSeconds > 0))
        {
            return;
        }

        _framePosition += (long)(deltaSeconds * SampleRate);
    }
}
