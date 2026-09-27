// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Diagnostics;
using Boutap.Audio;
using Boutap.Core.Pack;
using Boutap.Input;
using Xunit;

namespace Boutap.Input.Tests;

/// <summary>Une carte son simulee, dont on pilote la position a la main.</summary>
internal sealed class FakeAudioSource : IAudioPositionSource
{
    public double SampleRate { get; set; } = 48000.0;

    public long FramePosition { get; set; }

    public bool IsRunning { get; set; }
}

/// <summary>Conversion d'un horodatage materiel en temps de jeu.</summary>
public sealed class InputClockTests
{
    [Fact]
    public void Evenement_A_L_Ancre_Donne_Le_Temps_De_L_Ancre()
    {
        FakeAudioSource source = new() { FramePosition = 24000 };
        AudioClock audio = new(source);
        InputClock input = new(audio);

        Assert.Equal(0.5d, input.ToGameTime(audio.TimestampAtAnchor), 9);
    }

    [Fact]
    public void Evenement_Avant_L_Ancre_Donne_Un_Temps_Negatif()
    {
        // Un evenement d'evenement anterieur a l'ancre n'est pas absurde : il
        // arrive quand le joueur frappe dans les premiers millisecondes apres
        // le demarrage, ou que le tampon d'entree a ete rempli avant l'appel a
        // Resync.
        FakeAudioSource source = new();
        AudioClock audio = new(source);
        InputClock input = new(audio);

        long halfBlock = Stopwatch.Frequency / 200; // 5 ms

        Assert.Equal(-0.005d, input.ToGameTime(audio.TimestampAtAnchor - halfBlock), 6);
    }

    [Fact]
    public void Latence_D_Entree_Retarde_Le_Jugement()
    {
        FakeAudioSource source = new() { FramePosition = 48000 };
        AudioClock audio = new(source);
        InputClock input = new(audio) { LatencySeconds = 0.02 };

        Assert.Equal(0.98d, input.ToGameTime(audio.TimestampAtAnchor), 9);
    }

    [Fact]
    public void Latence_Supplementaire_S_Additionne_A_La_Latence_Retenue()
    {
        FakeAudioSource source = new() { FramePosition = 48000 };
        AudioClock audio = new(source);
        InputClock input = new(audio) { LatencySeconds = 0.02 };

        double gameTime = input.ToGameTime(audio.TimestampAtAnchor, 0.01);

        Assert.Equal(0.97d, gameTime, 9);
    }

    [Fact]
    public void Correction_Derive_S_Applique_Au_Temps_Ecoule()
    {
        FakeAudioSource source = new() { FramePosition = 48000 };
        AudioClock audio = new(source) { RateCorrection = 1.001 };
        InputClock input = new(audio);

        long oneSecond = Stopwatch.Frequency;

        // 100 ppm de correction sur une seconde ecoulee : 1 ms de plus sur
        // une seconde mesuree, donc 2,001 s au total puisque l'ancre est a 1 s.
        Assert.Equal(2.001d, input.ToGameTime(audio.TimestampAtAnchor + oneSecond), 6);
    }

    [Fact]
    public void Conversion_En_Echantillon_Utilise_L_Ancre_Audio()
    {
        FakeAudioSource source = new() { SampleRate = 44100 };
        AudioClock audio = new(source);
        InputClock input = new(audio);

        Assert.Equal(44100, input.ToAudioFrame(1.0));
        Assert.Equal(22050, input.ToAudioFrame(0.5));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.001)]
    [InlineData(0.25)]
    [InlineData(0.5)]
    public void Latence_Dans_Les_Bornes_Acceptee(double latency)
    {
        InputClock input = new(new AudioClock(new FakeAudioSource()));

        input.LatencySeconds = latency;

        Assert.Equal(latency, input.LatencySeconds, 9);
    }

    [Theory]
    [InlineData(-0.001)]
    [InlineData(0.501)]
    [InlineData(1.0)]
    [InlineData(double.NaN)]
    public void Latence_Hors_Bornes_Refusee(double latency)
    {
        InputClock input = new(new AudioClock(new FakeAudioSource()));

        Assert.Throws<ArgumentOutOfRangeException>(() => input.LatencySeconds = latency);
    }

    [Fact]
    public void Latence_Mesuree_Est_La_Mediane_Et_Non_La_Moyenne()
    {
        // 31 echantillons : le deuxieme est aberrant, comme le premier
        // evenement d'une session, qui paie le chargement de la page.
        long[] samples = new long[31];
        long tick = Stopwatch.Frequency / 1000; // 1 ms
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = tick;
        }

        samples[1] = tick * 900;
        samples[29] = tick * 400;

        double median = InputClock.MeasureLatencySeconds(samples);

        Assert.Equal(0.001d, median, 6);
    }

    [Fact]
    public void Latence_Mesuree_Echoue_Sans_Echantillon()
    {
        Assert.Throws<ArgumentException>(() => InputClock.MeasureLatencySeconds([]));
    }

    [Fact]
    public void Latence_Mesuree_N_Est_Pas_Impactee_Par_L_Ordre()
    {
        long[] ascending = [10L, 20L, 30L, 40L, 50L];
        long[] descending = [50L, 40L, 30L, 20L, 10L];

        Assert.Equal(
            InputClock.MeasureLatencySeconds(ascending),
            InputClock.MeasureLatencySeconds(descending),
            12);
    }

    [Fact]
    public void Horodatage_Courant_Est_Monotone()
    {
        long first = InputClock.Now();
        long second = InputClock.Now();

        Assert.True(second >= first, "Stopwatch.GetTimestamp() ne recule pas.");
    }

    [Fact]
    public void Constructeur_Refuse_Une_Horloge_Nulle()
    {
        Assert.Throws<ArgumentNullException>(() => new InputClock(null!));
    }

    [Fact]
    public void Evenement_Porte_Son_Temps_Son_Touche_Et_Son_Type()
    {
        InputEvent press = new(1234L, InputEventKind.KeyDown, KeyBinding.Grid(4));

        Assert.Equal(1234L, press.Timestamp);
        Assert.Equal(InputEventKind.KeyDown, press.Kind);
        Assert.Equal(KeyBinding.Grid(4), press.Key);
        Assert.Equal("4", press.Key.ToString());
        Assert.Equal(press, new InputEvent(1234L, InputEventKind.KeyDown, KeyBinding.Grid(4)));
        Assert.NotEqual(press, new InputEvent(1235L, InputEventKind.KeyDown, KeyBinding.Grid(4)));
        Assert.NotEqual(press, new InputEvent(1234L, InputEventKind.KeyUp, KeyBinding.Grid(4)));
    }
}
