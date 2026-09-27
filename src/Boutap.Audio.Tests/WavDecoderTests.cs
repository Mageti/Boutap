// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Buffers.Binary;
using System.Text;
using Boutap.Audio;
using Xunit;

namespace Boutap.Audio.Tests;

/// <summary>Lecture des en-tetes WAV et conversion vers la forme canonique.</summary>
public sealed class WavDecoderTests
{
    // Un quantum d'un bit dans chaque format entier : c'est la precision
    // maximale qu'un echantillon peut avoir apres conversion.
    private const float Q16 = 1f / 32768f;
    private const float Q24 = 1f / 8388608f;
    private const float Q32 = 1e-7f;

    [Fact]
    public void EnTete_Pcm16_Mono_Est_Lu()
    {
        byte[] file = WavBuilder.Pcm16(channels: 1, sampleRate: 44100)
            .WithRawData([0x00, 0x00, 0x34, 0x12])
            .Build();

        WavInfo info = ReadInfo(file);

        Assert.Equal(44100, info.SampleRate);
        Assert.Equal(1, info.Channels);
        Assert.Equal(16, info.BitsPerSample);
        Assert.False(info.IsFloat);
        Assert.Equal(2, info.FrameCount);
        Assert.Equal(44, info.DataOffset);
        Assert.Equal(4, info.DataLength);
        Assert.Equal(2, info.SampleCount);
    }

    [Fact]
    public void Pcm16_Est_Divise_Par_32768()
    {
        byte[] file = WavBuilder.Pcm16()
            .WithRawData([0x00, 0x80, 0x00, 0x00, 0xFF, 0x7F, 0x00, 0x00])
            .Build();

        float[] samples = Decode(file);

        Assert.Equal(4, samples.Length);
        Assert.Equal(-1f, samples[0], Q16);
        Assert.Equal(0f, samples[1]);
        Assert.Equal(32767f / 32768f, samples[2], Q16);
        Assert.Equal(0f, samples[3]);
    }

    [Fact]
    public void Pcm8_Est_Non_Signe_Et_Recentre_Sur_128()
    {
        // Le seul format entier non signe : une erreur de signe ici
        // inverserait tout le signal d'un WAV 8 bits.
        byte[] file = WavBuilder.Pcm8()
            .WithRawData([0x80, 0x00, 0xFF, 0x40])
            .Build();

        float[] samples = Decode(file);

        Assert.Equal(4, samples.Length);
        Assert.Equal(0f, samples[0]);
        Assert.Equal(-1f, samples[1]);
        Assert.Equal(127f / 128f, samples[2], Q16);
        Assert.Equal(-0.5f, samples[3], Q16);
        Assert.Equal(0.5f, Decode(WavBuilder.Pcm8().WithRawData([0xC0]).Build())[0], Q16);
    }

    [Fact]
    public void Pcm24_Lit_Le_Signe_Du_Third_Octet()
    {
        byte[] file = WavBuilder.Pcm24()
            .WithRawData([0x00, 0x00, 0x80, 0xFF, 0xFF, 0x7F, 0x00, 0x00, 0x00])
            .Build();

        float[] samples = Decode(file);

        Assert.Equal(3, samples.Length);
        Assert.Equal(-1f, samples[0], Q24);
        Assert.Equal(8388607f / 8388608f, samples[1], Q24);
        Assert.Equal(0f, samples[2]);
    }

    [Fact]
    public void Pcm32_Entier_Est_Divise_Par_2147483648()
    {
        byte[] file = WavBuilder.Pcm32()
            .WithRawData([0x00, 0x00, 0x00, 0x80, 0xFF, 0xFF, 0xFF, 0x7F])
            .Build();

        float[] samples = Decode(file);

        Assert.Equal(-1f, samples[0], Q32);
        Assert.Equal(2147483647d / 2147483648d, samples[1], Q32);
    }

    [Fact]
    public void Flottant32_Reste_Tel_Qu_Et_Ec_Systeme_En_Dehors()
    {
        using MemoryStream payload = new();
        using (BinaryWriter writer = new(payload, Encoding.ASCII, leaveOpen: true))
        {
            WavBuilder.WriteFloat32(writer, 0.5f);
            WavBuilder.WriteFloat32(writer, 1f);
            WavBuilder.WriteFloat32(writer, -1f);
            WavBuilder.WriteFloat32(writer, 2.5f);
            WavBuilder.WriteFloat32(writer, -2.5f);
            WavBuilder.WriteFloat32(writer, float.NaN);
            WavBuilder.WriteFloat32(writer, float.PositiveInfinity);
        }

        float[] samples = Decode(WavBuilder.Float32().WithRawData(payload.ToArray()).Build());

        Assert.Equal(0.5f, samples[0]);
        Assert.Equal(1f, samples[1]);
        Assert.Equal(-1f, samples[2]);

        // Un fichier audio « propre » ne depasse jamais 1, mais un encodeur
        // qui sature ou un mixage mal normalise si. On borne plutot que de
        // laisser une valeur hors echelle traverser la forme canonique, dont
        // depend le SHA-256 du manifeste.
        Assert.Equal(1f, samples[3]);
        Assert.Equal(-1f, samples[4]);

        // Un NaN se propagerait dans toute la transformee de Fourier du
        // generateur ; il est donc transforme en zero.
        Assert.Equal(0f, samples[5]);
        Assert.Equal(1f, samples[6]);
    }

    [Fact]
    public void Flottant64_Et_Reconverti_En_Float32_Canonique()
    {
        using MemoryStream payload = new();
        using (BinaryWriter writer = new(payload, Encoding.ASCII, leaveOpen: true))
        {
            WavBuilder.WriteFloat64(writer, 0.25d);
            WavBuilder.WriteFloat64(writer, -3d);
        }

        float[] samples = Decode(WavBuilder.Float64().WithRawData(payload.ToArray()).Build());

        Assert.Equal(0.25f, samples[0]);
        Assert.Equal(-1f, samples[1]);
    }

    [Fact]
    public void Stereo_Est_Entrelace_Dans_L_Ordre_Des_Canaux()
    {
        byte[] file = WavBuilder.Pcm16(channels: 2)
            .WithRawData([0x00, 0x00, 0x00, 0x80, 0xFF, 0x7F, 0x00, 0x00])
            .Build();

        WavInfo info = ReadInfo(file);
        float[] samples = Decode(file);

        Assert.Equal(2, info.FrameCount);
        Assert.Equal(4, samples.Length);
        Assert.Equal(0f, samples[0]);
        Assert.Equal(-1f, samples[1], Q16);
        Assert.Equal(32767f / 32768f, samples[2], Q16);
        Assert.Equal(0f, samples[3]);
    }

    [Fact]
    public void Mono_Fait_La_Moyenne_Des_Canaux()
    {
        byte[] file = WavBuilder.Pcm16(channels: 2)
            .WithRawData([0x00, 0x00, 0x00, 0x80, 0x00, 0x40, 0x00, 0x00])
            .Build();

        using MemoryStream stream = new(file);
        WavInfo info = WavDecoder.ReadInfo(stream);

        float[] mono = WavDecoder.DecodeToMono(stream, info);

        // (0 ; -1) puis (0,5 ; 0) : une moyenne, et non un simple canal.
        Assert.Equal(2, mono.Length);
        Assert.Equal(-0.5f, mono[0], Q16);
        Assert.Equal(0.25f, mono[1], Q16);
    }

    [Fact]
    public void Mono_Reste_Lisible_Quand_Il_N_Ya_Qu_Un_Canal()
    {
        byte[] file = WavBuilder.Pcm16().WithRawData([0x00, 0x00, 0x34, 0x12]).Build();

        using MemoryStream stream = new(file);
        WavInfo info = WavDecoder.ReadInfo(stream);

        float[] mono = WavDecoder.DecodeToMono(stream, info);

        Assert.Equal(2, mono.Length);
        Assert.Equal(4660f / 32768f, mono[1], Q16);
    }

    [Fact]
    public void Chunk_Inconnu_Et_Chunk_Debourre_Ignores()
    {
        // 'LIST' est un chunk reel mais inutile ici ; il est de longueur
        // impaire pour exercer l'octet de debourrage qu'impose la norme.
        byte[] file = WavBuilder.Pcm16()
            .WithExtraChunk("LIST", [0x49, 0x4E, 0x46])
            .WithExtraChunk("fact", [])
            .WithRawData([0x00, 0x00, 0x34, 0x12])
            .Build();

        WavInfo info = ReadInfo(file);

        Assert.Equal(2, info.FrameCount);
        Assert.Equal(2, Decode(file).Length);
        Assert.Equal(4660f / 32768f, Decode(file)[1], Q16);
    }

    [Fact]
    public void Format_Extensible_Entier_Est_Accepte()
    {
        byte[] file = WavBuilder.Pcm16(channels: 2).AsExtensible()
            .WithRawData([0x00, 0x00, 0x00, 0x80, 0xFF, 0x7F, 0x34, 0x12])
            .Build();

        WavInfo info = ReadInfo(file);
        float[] samples = Decode(file);

        Assert.Equal(16, info.BitsPerSample);
        Assert.False(info.IsFloat);
        Assert.Equal(2, info.FrameCount);
        Assert.Equal(4, samples.Length);
        Assert.Equal(-1f, samples[1], Q16);
        Assert.Equal(4660f / 32768f, samples[3], Q16);
    }

    [Fact]
    public void Format_Extensible_Flottant_Est_Accepte()
    {
        using MemoryStream payload = new();
        using (BinaryWriter writer = new(payload, Encoding.ASCII, leaveOpen: true))
        {
            WavBuilder.WriteFloat32(writer, 0.75f);
        }

        byte[] file = WavBuilder.Float32().AsExtensible().WithRawData(payload.ToArray()).Build();

        Assert.True(ReadInfo(file).IsFloat);
        Assert.Equal(0.75f, Decode(file)[0]);
    }

    [Fact]
    public void Sous_Format_Extensible_Non_Gere_Refuse()
    {
        byte[] file = WavBuilder.Pcm16().AsExtensible().WithRawData([]).Build();
        file[44] = 0x11; // ni PCM ni IEEE float dans les 4 premiers octets

        WavFormatException error = Assert.Throws<WavFormatException>(
            () => WavDecoder.ReadInfo(new MemoryStream(file)));
        Assert.Contains("Sous-format", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Bits_Valides_Incoherents_Refuses()
    {
        byte[] file = WavBuilder.Pcm16().AsExtensible().WithRawData([]).Build();
        file[38] = 8; // 8 bits valides dans un conteneur de 16 bits
        file[39] = 0;

        WavFormatException error = Assert.Throws<WavFormatException>(
            () => WavDecoder.ReadInfo(new MemoryStream(file)));
        Assert.Contains("Bits valides", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Chunk_Format_Extensible_Trop_Court_Refuse()
    {
        byte[] file = WavBuilder.Pcm16().AsExtensible().WithDeclaredFormatSize(24).WithRawData([]).Build();

        WavFormatException error = Assert.Throws<WavFormatException>(
            () => WavDecoder.ReadInfo(new MemoryStream(file)));
        Assert.Contains("extensible", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Data_Tronque_Rend_Ce_Qu_Il_Reste()
    {
        // L'en-tete annonce 4 echantillons, le fichier n'en contient que 2.
        byte[] file = WavBuilder.Pcm16()
            .WithDeclaredDataLength(8)
            .WithRawData([0x00, 0x00, 0x34, 0x12])
            .Build();

        WavInfo info = ReadInfo(file);
        float[] samples = Decode(file);

        Assert.Equal(4, info.FrameCount);
        Assert.Equal(2, samples.Length);
        Assert.Equal(0f, samples[0]);
        Assert.Equal(4660f / 32768f, samples[1], Q16);
    }

    [Fact]
    public void Data_Tronque_Au_Milieu_D_Un_Echantillon_Le_Jette()
    {
        // 5 octets : deux echantillons de 16 bits et un demi.
        byte[] file = WavBuilder.Pcm16()
            .WithDeclaredDataLength(5)
            .WithRawData([0x00, 0x00, 0x34, 0x12, 0xFF])
            .Build();

        Assert.Equal(2, Decode(file).Length);
    }

    [Fact]
    public void Signature_Absente_Refusee()
    {
        byte[] file = WavBuilder.Pcm16().WithRawData([]).Build();
        file[0] = (byte)'X';

        WavFormatException error = Assert.Throws<WavFormatException>(
            () => WavDecoder.ReadInfo(new MemoryStream(file)));
        Assert.Contains("RIFF", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Type_WAVE_Absent_Refuse()
    {
        byte[] file = WavBuilder.Pcm16().WithRawData([]).Build();
        file[8] = (byte)'X';

        WavFormatException error = Assert.Throws<WavFormatException>(
            () => WavDecoder.ReadInfo(new MemoryStream(file)));
        Assert.Contains("WAVE", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Fichier_Trop_Court_Refuse()
    {
        WavFormatException error = Assert.Throws<WavFormatException>(
            () => WavDecoder.ReadInfo(new MemoryStream([1, 2, 3])));
        Assert.Contains("trop court", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Chunk_Format_Absent_Refuse()
    {
        byte[] file = WavBuilder.Pcm16().WithoutFormat().WithRawData([]).Build();

        WavFormatException error = Assert.Throws<WavFormatException>(
            () => WavDecoder.ReadInfo(new MemoryStream(file)));
        Assert.Contains("fmt", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Chunk_Data_Absent_Refuse()
    {
        byte[] file = WavBuilder.Pcm16().WithoutData().Build();

        WavFormatException error = Assert.Throws<WavFormatException>(
            () => WavDecoder.ReadInfo(new MemoryStream(file)));
        Assert.Contains("data", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Chunk_Format_Trop_Court_Refuse()
    {
        byte[] file = WavBuilder.Pcm16().WithDeclaredFormatSize(12).WithRawData([]).Build();

        WavFormatException error = Assert.Throws<WavFormatException>(
            () => WavDecoder.ReadInfo(new MemoryStream(file)));
        Assert.Contains("fmt", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_Non_Gere_Refuse()
    {
        // 0x0011 est l'ADPCM Microsoft : hors de portee de cette version, qui
        // n'embarque que du PCM et de l'IEEE float.
        byte[] file = WavBuilder.Pcm16().WithFormatTag(0x0011).WithRawData([]).Build();

        WavFormatException error = Assert.Throws<WavFormatException>(
            () => WavDecoder.ReadInfo(new MemoryStream(file)));
        Assert.Contains("non gere", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Largeur_Non_Geree_Refusee()
    {
        byte[] file = WavBuilder.Pcm16().WithRawData([]).Build();
        file[34] = 12; // 12 bits n'existent pas en WAV PCM

        WavFormatException error = Assert.Throws<WavFormatException>(
            () => WavDecoder.ReadInfo(new MemoryStream(file)));
        Assert.Contains("non geree", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Frequence_Hors_Limites_Refusee()
    {
        byte[] file = WavBuilder.Pcm16().WithRawData([]).Build();
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(24), 4096);

        WavFormatException error = Assert.Throws<WavFormatException>(
            () => WavDecoder.ReadInfo(new MemoryStream(file)));
        Assert.Contains("Frequence", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Nombre_De_Canaux_Hors_Limites_Refuse()
    {
        byte[] file = WavBuilder.Pcm16().WithRawData([]).Build();
        file[22] = 0;
        file[23] = 0;

        WavFormatException error = Assert.Throws<WavFormatException>(
            () => WavDecoder.ReadInfo(new MemoryStream(file)));
        Assert.Contains("canaux", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Duree_Est_Derivee_De_L_Entete()
    {
        byte[] file = WavBuilder.Pcm16(sampleRate: 48000)
            .WithRawData(new byte[6 * 2])
            .Build();

        WavInfo info = ReadInfo(file);

        Assert.Equal(6, info.FrameCount);
        Assert.Equal(0.000125d, info.DurationSeconds, 9);
    }

    [Fact]
    public void Fichier_De_Zero_Octet_N_Est_Pas_Refuse_Par_L_Entete()
    {
        // Un 'data' vide est un fichier valide : c'est le cas d'un pack dont
        // l'audio a ete tronque a zero, que l'audit doit signaler par la
        // duree, pas par un refus de lecture.
        WavInfo info = ReadInfo(WavBuilder.Pcm16().Build());

        Assert.Equal(0, info.FrameCount);
        Assert.Equal(0d, info.DurationSeconds);
    }

    private static WavInfo ReadInfo(byte[] file) => WavDecoder.ReadInfo(new MemoryStream(file));

    private static float[] Decode(byte[] file)
    {
        using MemoryStream stream = new(file);
        WavInfo info = WavDecoder.ReadInfo(stream);
        return WavDecoder.DecodeToCanonical(stream, info);
    }
}
