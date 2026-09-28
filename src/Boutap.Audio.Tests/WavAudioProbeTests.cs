// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Runtime.InteropServices;
using Boutap.Core.Common;
using Boutap.Core.Pack;
using Xunit;

namespace Boutap.Audio.Tests;

/// <summary>
/// L'audio d'un pack arrive par une entree d'archive ZIP, qui n'est pas
/// seekable. Ces tests verrouillent ce cas : c'est lui qui a fait echouer la
/// premiere version de la sonde.
/// </summary>
public sealed class WavAudioProbeTests
{
    [Fact]
    public void La_Sonde_Accepte_Un_Flux_Qui_N_Est_Pas_Searchable()
    {
        byte[] wav = WavBuilder.Pcm16(channels: 2, sampleRate: 44100).WithRawData(Samples(44100 / 10)).Build();
        WavAudioProbe probe = new();

        using ForwardOnlyStream stream = new(wav);
        bool ok = probe.TryInspect(stream, out PackAudioFacts facts);

        Assert.True(ok);
        Assert.Equal(44100, facts.SampleRate);
        Assert.Equal(2, facts.Channels);
        Assert.Equal(16, facts.BitsPerSample);
    }

    [Fact]
    public void L_Empreinte_Est_Celle_De_La_Forme_Canonique()
    {
        byte[] wav = WavBuilder.Pcm16(channels: 1, sampleRate: 22050).WithRawData(Samples(22050 / 4)).Build();
        WavAudioProbe probe = new();

        using ForwardOnlyStream stream = new(wav);
        Assert.True(probe.TryInspect(stream, out PackAudioFacts facts));

        using MemoryStream source = new(wav, writable: false);
        float[] canonical = WavDecoder.DecodeToCanonical(source, WavDecoder.ReadInfo(source));

        Assert.Equal(Sha256Hex.OfBytesHex(MemoryMarshal.AsBytes<float>(canonical)), facts.Sha256Hex);
    }

    [Fact]
    public void Une_Duree_Est_Deduite_De_L_Entete()
    {
        byte[] wav = WavBuilder.Pcm16(channels: 1, sampleRate: 8000).WithRawData(Samples(8000)).Build();
        WavAudioProbe probe = new();

        using ForwardOnlyStream stream = new(wav);
        Assert.True(probe.TryInspect(stream, out PackAudioFacts facts));

        Assert.Equal(1d, facts.DurationSeconds, 6);
    }

    [Fact]
    public void Un_Flux_Incomplet_Est_Rejete_Sans_Panique()
    {
        byte[] wav = WavBuilder.Pcm16().WithRawData(Samples(4800)).Build();
        WavAudioProbe probe = new();

        using ForwardOnlyStream truncated = new(wav[..40]);

        Assert.False(probe.TryInspect(truncated, out PackAudioFacts facts));
        Assert.Equal(default, facts);
    }

    [Fact]
    public void Des_Octets_Qui_N_Ont_Rien_A_Voir_Are_Refuses()
    {
        WavAudioProbe probe = new();

        using ForwardOnlyStream stream = new("ceci n'est pas un WAV"u8.ToArray());

        Assert.False(probe.TryInspect(stream, out _));
    }

    [Fact]
    public void Un_Flux_Vide_Est_Refuse()
    {
        WavAudioProbe probe = new();

        using ForwardOnlyStream stream = new([]);

        Assert.False(probe.TryInspect(stream, out _));
    }

    [Fact]
    public void La_Forme_Canonique_Est_Declaree()
    {
        // Le manifeste annonce une empreinte de l'audio decode : deux lecteurs
        // doivent pouvoir verifier ce que cela veut dire sans deviner.
        Assert.Equal("wav", WavAudioProbe.Name);
        Assert.Contains("[-1 ; 1]", WavAudioProbe.CanonicalFormDescription, StringComparison.Ordinal);
        Assert.Contains("32", WavAudioProbe.CanonicalFormDescription, StringComparison.Ordinal);
    }

    private static byte[] Samples(int count)
    {
        byte[] data = new byte[count * 2];
        for (int i = 0; i < count; i++)
        {
            short value = (short)(i * 37 % 30000 - 15000);
            data[i * 2] = (byte)(value & 0xFF);
            data[(i * 2) + 1] = (byte)((value >> 8) & 0xFF);
        }

        return data;
    }

    /// <summary>Flux qui ne sait faire ni <c>Seek</c> ni <c>Position</c>.</summary>
    /// <remarks>
    /// C'est exactement le contrat d'une entree de <see cref="System.IO.Compression.ZipArchive"/> :
    /// <c>CanSeek</c> vaut faux et toucher a <c>Position</c> leve. Sans ce
    /// conteneur, un test passerait sur un <c>MemoryStream</c> et le code
    /// reviendrait a briser les packs a la premiere execution reelle.
    /// </remarks>
    private sealed class ForwardOnlyStream : Stream
    {
        private readonly MemoryStream _inner;

        public ForwardOnlyStream(byte[] bytes) => _inner = new MemoryStream(bytes, writable: false);

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => _inner.Length;

        public override long Position
        {
            get => throw new NotSupportedException("Un flux d'archive ne se positionne pas.");
            set => throw new NotSupportedException("Un flux d'archive ne se positionne pas.");
        }

        public override void Flush() => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

        public override int Read(Span<byte> buffer) => _inner.Read(buffer);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
