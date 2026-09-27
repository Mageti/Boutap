// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Boutap.Audio;
using Xunit;

namespace Boutap.Audio.Tests;

/// <summary>Une carte son simulee, dont on pilote la position a la main.</summary>
internal sealed class FakeAudioSource : IAudioPositionSource
{
    public double SampleRate { get; set; } = 48000.0;

    public long FramePosition { get; set; }

    public bool IsRunning { get; set; }
}

/// <summary>
/// L'horloge de jeu : extrapolation plafonne, resynchronisation, decalages.
/// </summary>
/// <remarks>
/// Ces tests n'attendent pas : ils exploitent le fait que l'extrapolation est
/// plafonnee. Apres une attente plus longue qu'un bloc, la position doit etre
/// exactement le plafond, quelle que soit la duree reelle de l'attente. C'est
/// une propriete deterministe, la ou une comparaison « a peu pres » serait
/// une course contre la resolution de <c>Stopwatch</c>.
/// </remarks>
public sealed class AudioClockTests
{
    [Fact]
    public void Position_Initiale_Vaut_La_Position_Du_Materiel()
    {
        FakeAudioSource source = new() { FramePosition = 48000 };
        AudioClock clock = new(source);

        Assert.Equal(1.0d, clock.PositionSecondsAtAnchor, 9);
        Assert.Equal(48000, clock.FramePosition);
    }

    [Fact]
    public void Extrapolation_Est_Plafonnee_A_Un_Bloc()
    {
        // 1 echantillon de bloc : le plafond vaut 20,8 microsecondes, donc
        // n'importe quelle attente mesurable le depasse.
        FakeAudioSource source = new();
        AudioClock clock = new(source, blockFrames: 1);

        Thread.Sleep(5);
        double position = clock.PositionSeconds;

        Assert.Equal(1.0d / 48000.0d, position, 12);
    }

    [Fact]
    public void Position_N_Est_Jamais_Avant_L_Ancre()
    {
        FakeAudioSource source = new();
        AudioClock clock = new(source, blockFrames: 1);

        Assert.InRange(clock.PositionSeconds, 0.0d, 1.0d / 48000.0d);
    }

    [Fact]
    public void Resync_Reprend_La_Position_Reellement_Rapportee()
    {
        FakeAudioSource source = new() { FramePosition = 96000 };
        AudioClock clock = new(source);

        Assert.Equal(2.0d, clock.PositionSecondsAtAnchor, 9);

        source.FramePosition = 100800;
        clock.Resync();

        Assert.Equal(2.1d, clock.PositionSecondsAtAnchor, 9);
        Assert.Equal(100800, clock.FramePosition);
    }

    [Fact]
    public void Resync_Elimine_L_Erreur_D_Extrapolation()
    {
        FakeAudioSource source = new();
        AudioClock clock = new(source, blockFrames: 1);

        Thread.Sleep(5);
        Assert.Equal(1.0d / 48000.0d, clock.PositionSeconds, 12);

        // La carte son n'a pas avance : on resynchronise, et l'ecart
        // accumule disparait au lieu de rester.
        clock.Resync();

        Assert.Equal(0.0d, clock.PositionSecondsAtAnchor, 12);
        Assert.InRange(clock.PositionSeconds, 0.0d, 1.0d / 48000.0d);
    }

    [Fact]
    public void Decalage_Utilisateur_Retarde_Le_Jugement()
    {
        FakeAudioSource source = new();
        AudioClock clock = new(source, blockFrames: 1) { UserOffsetSeconds = 0.08 };

        Assert.Equal(0.08d, clock.JudgementTimeSeconds - clock.PositionSeconds, 12);
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(1.0)]
    [InlineData(0.0)]
    [InlineData(-0.25)]
    [InlineData(0.25)]
    public void Decalage_Utilisateur_Accepte_Moins_Une_Seconde(double offset)
    {
        FakeAudioSource source = new();
        AudioClock clock = new(source) { UserOffsetSeconds = offset };

        Assert.Equal(offset, clock.UserOffsetSeconds, 9);
    }

    [Theory]
    [InlineData(1.001)]
    [InlineData(-1.001)]
    [InlineData(2.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Decalage_Utilisateur_Hors_Limites_Refuse(double offset)
    {
        FakeAudioSource source = new();
        AudioClock clock = new(source);

        Assert.Throws<ArgumentOutOfRangeException>(() => clock.UserOffsetSeconds = offset);
    }

    [Fact]
    public void Correction_Derive_Accepte_La_Pente_Mesuree()
    {
        FakeAudioSource source = new();
        AudioClock clock = new(source, blockFrames: 1) { RateCorrection = 1.0001 };

        // 100 ppm de derive : c'est l'ordre de grandeur d'un crystal audio.
        Thread.Sleep(5);
        double expected = (1.0d / 48000.0d) * 1.0001;

        Assert.Equal(expected, clock.PositionSeconds, 12);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(1.2)]
    [InlineData(double.NaN)]
    public void Correction_Derive_Hors_Limites_Refusee(double rate)
    {
        FakeAudioSource source = new();
        AudioClock clock = new(source);

        Assert.Throws<ArgumentOutOfRangeException>(() => clock.RateCorrection = rate);
    }

    [Fact]
    public void Frequence_Absurde_Replie_Sur_Le_Defaut()
    {
        // Une carte son qui annonce 0 Hz ne doit pas produire une horloge
        // qui divise par zero : le defaut est preferable a une exception au
        // demarrage.
        FakeAudioSource source = new() { SampleRate = 0.0 };
        AudioClock clock = new(source);

        Assert.Equal(AudioClock.DefaultSampleRate, clock.SampleRate, 9);
        Assert.Equal(48000, clock.SecondsToFrame(1.0));
    }

    [Theory]
    [InlineData(0.0d, 0L)]
    [InlineData(1.0d, 48000L)]
    [InlineData(2.5d, 120000L)]
    [InlineData(-1.0d, -48000L)]
    [InlineData(0.123456d, 5926L)]
    public void Conversion_Images_Secondes_Est_Reciproque(double seconds, long frames)
    {
        FakeAudioSource source = new();
        AudioClock clock = new(source);

        Assert.Equal(frames, clock.SecondsToFrame(seconds));
        Assert.Equal((double)frames / 48000.0d, clock.FrameToSeconds(frames), 12);
    }

    [Fact]
    public void Constructeur_Refuse_Une_Source_Nulle()
    {
        Assert.Throws<ArgumentNullException>(() => new AudioClock(null!));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructeur_Refuse_Un_Bloc_Nul_ou_Negatif(int blockFrames)
    {
        FakeAudioSource source = new();

        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioClock(source, blockFrames));
    }
}
