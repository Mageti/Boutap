// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Buffers.Binary;

namespace Boutap.Audio;

/// <summary>
/// Lecteur WAV entierement managé.
/// </summary>
/// <remarks>
/// <para>
/// Le decodage de l'audio est ici, en C#, et non dans une bibliotheque
/// native : le projet n'embarque que du code qu'il peut compiler et auditer,
/// et miniaudio n'arrive qu'en S3.1 pour les formats compresses. Un WAV est
/// suffisant pour toute la V0.1, qui ne fait que lire des fixtures de test et
/// des packs de demonstration.
/// </para>
/// <para>
/// <see cref="DecodeToCanonical"/> produit la forme canonique dont le
/// manifeste mesure le SHA-256 : flottants 32 bits entre -1 et 1, entrelaces,
/// ordre des canaux d'origine. Toute evolution de cette forme changerait le
/// hash de tous les packs deja publies, donc n'est pas anodine.
/// </para>
/// </remarks>
public static class WavDecoder
{
    private const int FormatPcm = 0x0001;
    private const int FormatIeeeFloat = 0x0003;
    private const int FormatExtensible = 0xFFFE;

    // GUID KSDATAFORMAT_SUBTYPE_PCM et _IEEE_FLOAT, compares sur leurs 4
    // premiers octets dans l'ordre du fichier (donc petit-boutiste).
    private const uint SubtypePcm = 0x00000001;
    private const uint SubtypeIeeeFloat = 0x00000003;

    /// <summary>Lit l'en-tete et la description des chunks d'un WAV.</summary>
    /// <param name="path">Chemin du fichier.</param>
    /// <returns>Ce que l'en-tete declare.</returns>
    /// <exception cref="WavFormatException">Le fichier n'est pas un WAV lisible.</exception>
    public static WavInfo ReadInfo(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return ReadInfo(new FileInfo(path));
    }

    /// <summary>Lit l'en-tete et la description des chunks d'un WAV.</summary>
    /// <param name="file">Fichier a inspecter.</param>
    /// <returns>Ce que l'en-tete declare.</returns>
    /// <exception cref="WavFormatException">Le fichier n'est pas un WAV lisible.</exception>
    public static WavInfo ReadInfo(FileInfo file)
    {
        ArgumentNullException.ThrowIfNull(file);

        try
        {
            using FileStream stream = file.OpenRead();
            return ReadInfo(stream);
        }
        catch (WavFormatException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new WavFormatException($"Lecture impossible de « {file.FullName} ».", ex);
        }
    }

    /// <summary>Lit l'en-tete et la description des chunks d'un WAV en memoire.</summary>
    /// <param name="stream">Flux positionne au debut du fichier.</param>
    /// <returns>Ce que l'en-tete declare.</returns>
    /// <exception cref="WavFormatException">Le flux n'est pas un WAV lisible.</exception>
    public static WavInfo ReadInfo(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using BinaryReader reader = new(stream, System.Text.Encoding.ASCII, leaveOpen: true);

        if (stream.Length < 12)
        {
            throw new WavFormatException("Fichier trop court pour etre un WAV.");
        }

        if (ReadFourCc(reader) != "RIFF")
        {
            throw new WavFormatException("Signature 'RIFF' absente : ce n'est pas un fichier WAV.");
        }

        _ = reader.ReadUInt32();

        if (ReadFourCc(reader) != "WAVE")
        {
            throw new WavFormatException("Le type 'WAVE' est absent : ce n'est pas un fichier WAV.");
        }

        int channels = 0;
        int sampleRate = 0;
        int bitsPerSample = 0;
        bool isFloat = false;
        bool haveFormat = false;
        long dataOffset = -1;
        long dataLength = 0;

        while (stream.Position + 8 <= stream.Length)
        {
            string chunkId = ReadFourCc(reader);
            uint chunkSize = reader.ReadUInt32();
            long chunkStart = stream.Position;

            if (chunkId == "fmt ")
            {
                if (chunkSize < 16)
                {
                    throw new WavFormatException($"Le chunk 'fmt ' est trop court ({chunkSize} octets).");
                }

                int formatTag = reader.ReadUInt16();
                channels = reader.ReadUInt16();
                sampleRate = checked((int)reader.ReadUInt32());
                _ = reader.ReadUInt32();
                _ = reader.ReadUInt16();
                bitsPerSample = reader.ReadUInt16();

                if (formatTag == FormatExtensible)
                {
                    if (chunkSize < 40)
                    {
                        throw new WavFormatException(
                            $"Le chunk 'fmt ' extensible est trop court ({chunkSize} octets).");
                    }

                    // L'extension ajoute cbSize, wValidBitsPerSample,
                    // dwChannelMask (4 octets) puis le GUID du sous-format.
                    // Sauter cbSize, ou lire le masque sur 2 octets, decale
                    // tout le reste de 2 ou 4 octets et fait rater le
                    // sous-format — c'est-a-dire le format reel du fichier.
                    _ = reader.ReadUInt16(); // cbSize, toujours 22
                    int validBits = reader.ReadUInt16();
                    _ = reader.ReadUInt32(); // dwChannelMask
                    uint subformat = reader.ReadUInt32(); // 4 premiers octets du GUID
                    isFloat = subformat == SubtypeIeeeFloat;
                    if (subformat != SubtypePcm && !isFloat)
                    {
                        throw new WavFormatException(
                            $"Sous-format WAV non gere (0x{subformat:X8}). Seuls PCM et IEEE float le sont.");
                    }

                    if (validBits != 0 && validBits != bitsPerSample)
                    {
                        throw new WavFormatException(
                            $"Bits valides ({validBits}) incoherents avec la largeur stockee ({bitsPerSample}).");
                    }
                }
                else if (formatTag == FormatPcm)
                {
                    isFloat = false;
                }
                else if (formatTag == FormatIeeeFloat)
                {
                    isFloat = true;
                }
                else
                {
                    throw new WavFormatException(
                        $"Format WAV non gere (0x{formatTag:X4}). Le projet n'embarque que PCM et IEEE float.");
                }

                haveFormat = true;
            }
            else if (chunkId == "data")
            {
                dataOffset = chunkStart;
                dataLength = chunkSize;
            }

            // Le chunk suivant commence a l'offset aligne sur une frontiere
            // paire, que l'en-tete respecte ou non.
            long next = chunkStart + chunkSize;
            if ((next & 1) == 1)
            {
                next++;
            }

            // Un chunk vide est legal : next vaut alors chunkStart, qui est
            // deja 8 octets plus loin que la position precedente, donc la
            // lecture avance toujours. Comparer avec < plutot que <= evite de
            // Cas limite : un 'fact' de longueur nulle, que les encodeurs ecrivent
            // pourtant souvent.
            if (next < chunkStart || next > stream.Length)
            {
                break;
            }

            stream.Position = next;
        }

        if (!haveFormat)
        {
            throw new WavFormatException("Le chunk 'fmt ' est absent.");
        }

        if (dataOffset < 0)
        {
            throw new WavFormatException("Le chunk 'data' est absent.");
        }

        if (channels < 1 || channels > 8)
        {
            throw new WavFormatException($"Nombre de canaux hors limites ({channels}) : 1 a 8 sont acceptes.");
        }

        if (sampleRate < 8000 || sampleRate > 384000)
        {
            throw new WavFormatException($"Frequence d'echantillonnage hors limites ({sampleRate} Hz).");
        }

        bool supported = isFloat
            ? bitsPerSample is 32 or 64
            : bitsPerSample is 8 or 16 or 24 or 32;

        if (!supported)
        {
            throw new WavFormatException(
                $"Largeur d'echantillon non geree ({bitsPerSample} bits, " +
                $"{(isFloat ? "flottant" : "entier")}).");
        }

        int bytesPerSample = bitsPerSample / 8;
        long bytesPerFrame = (long)bytesPerSample * channels;
        long frameCount = dataLength / bytesPerFrame;

        return new WavInfo(sampleRate, channels, bitsPerSample, isFloat, frameCount, dataOffset, dataLength);
    }

    /// <summary>
    /// Decode un WAV vers la forme canonique : flottants 32 bits dans
    /// [-1 ; 1], entrelaces, ordre des canaux d'origine.
    /// </summary>
    /// <param name="stream">Flux positionne au debut du fichier.</param>
    /// <param name="info">Resultat de <see cref="ReadInfo(Stream)"/> sur le meme flux.</param>
    /// <returns>Les echantillons canoniques, tous canaux confondus.</returns>
    /// <exception cref="WavFormatException">Le flux n'est pas un WAV lisible.</exception>
    public static float[] DecodeToCanonical(Stream stream, WavInfo info)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(info);

        if (info.SampleCount > int.MaxValue)
        {
            throw new WavFormatException(
                $"Le fichier contient {info.SampleCount} echantillons, au-dela de ce que cette version lit.");
        }

        stream.Position = info.DataOffset;
        byte[] raw = new byte[info.DataLength];
        int read = 0;
        while (read < raw.Length)
        {
            int chunk = stream.Read(raw, read, raw.Length - read);
            if (chunk <= 0)
            {
                break;
            }

            read += chunk;
        }

        if (read < raw.Length)
        {
            // Un data tronque n'est pas une erreur fatale : on rend ce qu'il y
            // a. Les fixtures tronquees servent precisement a tester ce chemin.
            Array.Resize(ref raw, read);
        }

        int bytesPerSample = info.BitsPerSample / 8;

        // Un 'data' tronque n'est pas une erreur fatale : l'en-tete peut
        // annoncer plus d'octets que le fichier n'en contient, typiquement
        // quand un fichier a ete copie pendant son telechargement. On rend ce
        // qu'il y a. Un echantillon partiel, lui, est jete : il ne formerait
        // pas une image complete.
        int totalSamples = (int)info.SampleCount;
        int readable = raw.Length / bytesPerSample;
        if (readable < totalSamples)
        {
            totalSamples = readable;
        }

        float[] samples = new float[totalSamples];
        for (int i = 0; i < totalSamples; i++)
        {
            samples[i] = DecodeSample(raw, i * bytesPerSample, info);
        }

        return samples;
    }

    /// <summary>Decode un WAV en melee, par moyenne des canaux.</summary>
    /// <param name="stream">Flux positionne au debut du fichier.</param>
    /// <param name="info">Resultat de <see cref="ReadInfo(Stream)"/> sur le meme flux.</param>
    /// <returns>Un echantillon par image stereo.</returns>
    /// <exception cref="WavFormatException">Le flux n'est pas un WAV lisible.</exception>
    public static float[] DecodeToMono(Stream stream, WavInfo info)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(info);

        float[] interleaved = DecodeToCanonical(stream, info);
        if (info.Channels == 1)
        {
            return interleaved;
        }

        float[] mono = new float[info.FrameCount];
        for (long frame = 0; frame < info.FrameCount; frame++)
        {
            double sum = 0;
            for (int channel = 0; channel < info.Channels; channel++)
            {
                sum += interleaved[(frame * info.Channels) + channel];
            }

            mono[frame] = (float)(sum / info.Channels);
        }

        return mono;
    }

    /// <summary>Lit l'en-tete, puis decode en une passe.</summary>
    /// <param name="path">Chemin du fichier.</param>
    /// <returns>Les echantillons canoniques.</returns>
    /// <exception cref="WavFormatException">Le fichier n'est pas un WAV lisible.</exception>
    public static float[] DecodeFileToCanonical(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        using FileStream stream = File.OpenRead(path);
        WavInfo info = ReadInfo(stream);
        return DecodeToCanonical(stream, info);
    }

    private static float DecodeSample(byte[] raw, int offset, WavInfo info)
    {
        switch (info.BitsPerSample)
        {
            case 8 when !info.IsFloat:
                // Le 8 bits PCM est non signe, les autres sont signes.
                return (raw[offset] - 128) / 128f;
            case 16 when !info.IsFloat:
                return BinaryPrimitives.ReadInt16LittleEndian(raw.AsSpan(offset, 2)) / 32768f;
            case 24 when !info.IsFloat:
                return ReadInt24LittleEndian(raw.AsSpan(offset, 3)) / 8388608f;
            case 32 when !info.IsFloat:
                return BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(offset, 4)) / 2147483648f;
            case 32:
                return Clamp(BitConverter.Int32BitsToSingle(
                    BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(offset, 4))));
            case 64:
                return Clamp((float)BitConverter.Int64BitsToDouble(
                    BinaryPrimitives.ReadInt64LittleEndian(raw.AsSpan(offset, 8))));
            default:
                throw new WavFormatException(
                    $"Largeur d'echantillon non geree ({info.BitsPerSample} bits).");
        }
    }

    private static float Clamp(float value)
    {
        if (float.IsNaN(value))
        {
            return 0f;
        }

        if (value < -1f)
        {
            return -1f;
        }

        return value > 1f ? 1f : value;
    }

    private static int ReadInt24LittleEndian(ReadOnlySpan<byte> span)
    {
        int value = span[0] | (span[1] << 8) | (span[2] << 16);
        if ((value & 0x00800000) != 0)
        {
            value |= unchecked((int)0xFF000000);
        }

        return value;
    }

    private static string ReadFourCc(BinaryReader reader)
    {
        Span<char> buffer = stackalloc char[4];
        for (int i = 0; i < 4; i++)
        {
            byte value = reader.ReadByte();
            buffer[i] = (value >= 0x20 && value < 0x7F) ? (char)value : '?';
        }

        return new string(buffer);
    }
}
