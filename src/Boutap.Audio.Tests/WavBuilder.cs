// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Buffers.Binary;
using System.Text;

namespace Boutap.Audio.Tests;

/// <summary>
/// Assemble un fichier WAV octet par octet.
/// </summary>
/// <remarks>
/// Le decodeur est teste sur des octets ecrits a la main plutot que sur des
/// fichiers produits par un encodeur : ce qui compte est la table de
/// conversion, pas le fait qu'un encodeur et un decodeur partagent la meme
/// erreur. Les octets sont donnes tels quels via <see cref="WithRawData"/>,
/// ce qui permet d'atteindre les bornes que l'en-tete annonce.
/// </remarks>
internal sealed class WavBuilder
{
    private const int FormatPcm = 0x0001;
    private const int FormatIeeeFloat = 0x0003;
    private const int FormatExtensible = 0xFFFE;

    private readonly List<(string Id, byte[] Payload)> _extraChunks = [];
    private byte[] _data = [];
    private int _formatTag;
    private bool _extensible;
    private bool _omitFormat;
    private bool _omitData;
    private int _declaredDataLength = -1;
    private int _formatChunkSize = -1;

    private WavBuilder(int sampleRate, int channels, int bitsPerSample, bool isFloat)
    {
        SampleRate = sampleRate;
        Channels = channels;
        BitsPerSample = bitsPerSample;
        IsFloat = isFloat;
        _formatTag = isFloat ? FormatIeeeFloat : FormatPcm;
    }

    /// <summary>Frequence d'echantillonnage annoncee.</summary>
    public int SampleRate { get; }

    /// <summary>Nombre de canaux annonces.</summary>
    public int Channels { get; }

    /// <summary>Largeur d'echantillon annoncee.</summary>
    public int BitsPerSample { get; }

    /// <summary>Vrai si l'echantillon est annonce en virgule flottante.</summary>
    public bool IsFloat { get; }

    /// <summary>En-tete <c>fmt </c> en WAVE_FORMAT_EXTENSIBLE.</summary>
    public WavBuilder AsExtensible()
    {
        _extensible = true;
        _formatTag = FormatExtensible;
        return this;
    }

    /// <summary>Force le tag de format, y compris vers un format non gere.</summary>
    /// <param name="tag">Valeur ecrase dans le champ wFormatTag.</param>
    public WavBuilder WithFormatTag(int tag)
    {
        _formatTag = tag;
        return this;
    }

    /// <summary>Force la taille annoncee du chunk <c>fmt </c>.</summary>
    /// <param name="size">Taille en octets, par exemple 12 pour un chunk tronque.</param>
    public WavBuilder WithDeclaredFormatSize(int size)
    {
        _formatChunkSize = size;
        return this;
    }

    /// <summary>Retire le chunk <c>fmt </c> du fichier produit.</summary>
    public WavBuilder WithoutFormat()
    {
        _omitFormat = true;
        return this;
    }

    /// <summary>Retire le chunk <c>data</c> du fichier produit.</summary>
    public WavBuilder WithoutData()
    {
        _omitData = true;
        return this;
    }

    /// <summary>Ajoute un chunk quelconque avant le <c>data</c>.</summary>
    /// <param name="id">Identifiant de quatre caracteres.</param>
    /// <param name="payload">Contenu du chunk, de longueur quelconque.</param>
    public WavBuilder WithExtraChunk(string id, byte[] payload)
    {
        _extraChunks.Add((id, payload));
        return this;
    }

    /// <summary>Impose les octets du chunk <c>data</c>.</summary>
    /// <param name="data">Contenu brut, tel qu'il sera lu sur le disque.</param>
    public WavBuilder WithRawData(byte[] data)
    {
        _data = data;
        return this;
    }

    /// <summary>Annonce une taille de <c>data</c> differente de la taille reelle.</summary>
    /// <param name="length">Taille annoncee en octets, eventuellement superieure.</param>
    public WavBuilder WithDeclaredDataLength(int length)
    {
        _declaredDataLength = length;
        return this;
    }

    /// <summary>Un WAV PCM 8 bits, non signe comme l'impose la norme.</summary>
    /// <param name="channels">Nombre de canaux.</param>
    /// <param name="sampleRate">Frequence d'echantillonnage.</param>
    public static WavBuilder Pcm8(int channels = 1, int sampleRate = 48000) =>
        new(sampleRate, channels, 8, isFloat: false);

    /// <summary>Un WAV PCM 16 bits.</summary>
    /// <param name="channels">Nombre de canaux.</param>
    /// <param name="sampleRate">Frequence d'echantillonnage.</param>
    public static WavBuilder Pcm16(int channels = 1, int sampleRate = 48000) =>
        new(sampleRate, channels, 16, isFloat: false);

    /// <summary>Un WAV PCM 24 bits.</summary>
    /// <param name="channels">Nombre de canaux.</param>
    /// <param name="sampleRate">Frequence d'echantillonnage.</param>
    public static WavBuilder Pcm24(int channels = 1, int sampleRate = 48000) =>
        new(sampleRate, channels, 24, isFloat: false);

    /// <summary>Un WAV PCM 32 bits entier.</summary>
    /// <param name="channels">Nombre de canaux.</param>
    /// <param name="sampleRate">Frequence d'echantillonnage.</param>
    public static WavBuilder Pcm32(int channels = 1, int sampleRate = 48000) =>
        new(sampleRate, channels, 32, isFloat: false);

    /// <summary>Un WAV en virgule flottante 32 bits.</summary>
    /// <param name="channels">Nombre de canaux.</param>
    /// <param name="sampleRate">Frequence d'echantillonnage.</param>
    public static WavBuilder Float32(int channels = 1, int sampleRate = 48000) =>
        new(sampleRate, channels, 32, isFloat: true);

    /// <summary>Un WAV en virgule flottante 64 bits.</summary>
    /// <param name="channels">Nombre de canaux.</param>
    /// <param name="sampleRate">Frequence d'echantillonnage.</param>
    public static WavBuilder Float64(int channels = 1, int sampleRate = 48000) =>
        new(sampleRate, channels, 64, isFloat: true);

    /// <summary>Assemble le fichier.</summary>
    /// <returns>Les octets du WAV.</returns>
    public byte[] Build()
    {
        int blockAlign = (BitsPerSample / 8) * Channels;

        using MemoryStream buffer = new();
        using BinaryWriter writer = new(buffer, Encoding.ASCII, leaveOpen: true);

        WriteAscii(writer, "RIFF");
        writer.Write(0); // taille, corrigee plus bas
        WriteAscii(writer, "WAVE");

        if (!_omitFormat)
        {
            int size = _formatChunkSize >= 0
                ? _formatChunkSize
                : (_extensible ? 40 : 16);

            WriteAscii(writer, "fmt ");
            writer.Write(size);
            writer.Write((ushort)_formatTag);
            writer.Write((ushort)Channels);
            writer.Write((uint)SampleRate);
            writer.Write((uint)(SampleRate * blockAlign));
            writer.Write((ushort)blockAlign);
            writer.Write((ushort)BitsPerSample);

            if (_extensible && size >= 40)
            {
                writer.Write((ushort)22);                       // cbSize
                writer.Write((ushort)BitsPerSample);            // wValidBitsPerSample
                writer.Write(0x3fu);                            // dwChannelMask, 6 canaux devant
                WriteSubtypeGuid(writer, IsFloat);
            }

            // Un chunk 'fmt ' plus court que 16 octets laisse des octets de
            // remplissage eventuels, que le decodeur doit ignorer.
            for (int written = _extensible ? 40 : 16; written < size; written++)
            {
                writer.Write((byte)0);
            }
        }

        foreach ((string id, byte[] payload) in _extraChunks)
        {
            WriteAscii(writer, id);
            writer.Write(payload.Length);
            writer.Write(payload);
            if ((payload.Length & 1) == 1)
            {
                writer.Write((byte)0); // octet debourrage, exige par la norme
            }
        }

        if (!_omitData)
        {
            WriteAscii(writer, "data");
            writer.Write(_declaredDataLength >= 0 ? _declaredDataLength : _data.Length);
            writer.Write(_data);
        }

        writer.Flush();
        byte[] file = buffer.ToArray();

        BinaryPrimitives.WriteUInt32LittleEndian(
            file.AsSpan(4, 4), (uint)(file.Length - 8));
        return file;
    }

    /// <summary>Ecrit trois octets en petit-boutiste.</summary>
    /// <param name="writer">Destination.</param>
    /// <param name="value">Valeur sur 24 bits.</param>
    public static void WriteInt24(BinaryWriter writer, int value)
    {
        writer.Write((byte)value);
        writer.Write((byte)(value >> 8));
        writer.Write((byte)(value >> 16));
    }

    /// <summary>Ecrit un echantillon flottant 32 bits en petit-boutiste.</summary>
    /// <param name="writer">Destination.</param>
    /// <param name="value">Valeur a ecrire.</param>
    public static void WriteFloat32(BinaryWriter writer, float value) =>
        writer.Write(BitConverter.SingleToInt32Bits(value));

    /// <summary>Ecrit un echantillon flottant 64 bits en petit-boutiste.</summary>
    /// <param name="writer">Destination.</param>
    /// <param name="value">Valeur a ecrire.</param>
    public static void WriteFloat64(BinaryWriter writer, double value) =>
        writer.Write(BitConverter.DoubleToInt64Bits(value));

    private static void WriteAscii(BinaryWriter writer, string text)
    {
        foreach (char c in text)
        {
            writer.Write((byte)c);
        }
    }

    private static void WriteSubtypeGuid(BinaryWriter writer, bool isFloat)
    {
        // KSDATAFORMAT_SUBTYPE_PCM ou _IEEE_FLOAT : les 4 premiers octets
        // suffisent au decodeur, le reste est ecrit par acquis de conscience.
        writer.Write(isFloat ? 0x00000003u : 0x00000001u);
        writer.Write((ushort)0x0000);
        writer.Write((ushort)0x0010);
        writer.Write(new byte[] { 0x80, 0x00, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71 });
    }
}
