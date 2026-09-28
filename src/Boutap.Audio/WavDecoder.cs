// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Boutap.Core.Common;

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

        // La longueur n'est pas toujours connue. Une entree ZIP comprimee
        // donne un DeflateStream, dont Length leve NotSupportedException : se
        // fier a Length ferait refuser tout pack ecrit avec compression, donc
        // tous les packs que notre propre ecrivain produit.
        bool lengthKnown = TryGetLength(stream, out long length);

        if (lengthKnown && length < 12)
        {
            throw new WavFormatException("Fichier trop court pour etre un WAV.");
        }

        try
        {
            if (ReadFourCc(reader) != "RIFF")
            {
                throw new WavFormatException("Signature 'RIFF' absente : ce n'est pas un fichier WAV.");
            }

            _ = reader.ReadUInt32();

            if (ReadFourCc(reader) != "WAVE")
            {
                throw new WavFormatException("Le type 'WAVE' est absent : ce n'est pas un fichier WAV.");
            }
        }
        catch (EndOfStreamException ex)
        {
            throw new WavFormatException("Fichier trop court pour etre un WAV.", ex);
        }

        int channels = 0;
        int sampleRate = 0;
        int bitsPerSample = 0;
        bool isFloat = false;
        bool haveFormat = false;
        long dataOffset = -1;
        long dataLength = 0;

        // Un flux n'est pas toujours seekable : l'entree d'un ZIP l'est
        // rarement, et c'est pourtant par elle qu'arrivent les audios de pack.
        // On suit donc la position plutot que de la partager avec le flux, et
        // on s'arrete sur le premier 'data' lorsque le retour n'est pas
        // possible.
        bool canSeek = stream.CanSeek;
        long position = 12;

        long limit = lengthKnown ? length : long.MaxValue;

        while (position + 8 <= limit)
        {
            string chunkId;
            uint chunkSize;
            try
            {
                chunkId = ReadFourCc(reader);
                chunkSize = reader.ReadUInt32();
            }
            catch (EndOfStreamException)
            {
                // Fin de fichier au milieu d'un en-tete de chunk : il n'y a
                // plus rien a lire, ce qui est la sortie normale de la boucle.
                break;
            }

            position += 8;
            long chunkStart = position;

            // Octets du corps du chunk deja lus. Seul 'fmt ' a un corps qu'on
            // lit sur place ; tous les autres sont sautes d'un bloc. Sans ce
            // compte, on sauterait par-dessus ce qu'on vient de lire en
            // cherchant le chunk suivant, et le 'data' disparaitrait — mais
            // seulement sur un flux qu'on ne peut pas repositionner, donc
            // seulement sur l'audio d'un pack.
            int consumed = 0;

            if (chunkId == "fmt ")
            {
                if (chunkSize < 16)
                {
                    throw new WavFormatException($"Le chunk 'fmt ' est trop court ({chunkSize} octets).");
                }

                consumed = 16;

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
                    consumed = 28;
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

            position = chunkStart + consumed;

            // Le chunk suivant commence a l'offset aligne sur une frontiere
            // paire, que l'en-tete respecte ou non.
            long next = chunkStart + chunkSize;
            if ((next & 1) == 1)
            {
                next++;
            }

            // Un chunk de longueur nulle est legal : next vaut alors
            // chunkStart, qui est deja plus loin que la position precedente,
            // donc la lecture avance toujours. Comparer avec < plutot qu'avec
            // <= evite de s'arreter sur ces chunks, qu'ecrivent pourtant
            // beaucoup d'encodeurs — un 'fact' vide precede souvent 'data'.
            if (next < chunkStart || (lengthKnown && next > limit))
            {
                break;
            }

            if (!canSeek && chunkId == "data")
            {
                if (!haveFormat)
                {
                    throw new WavFormatException(
                        "Sur un flux qu'on ne peut pas repositionner, le chunk 'fmt ' doit preceder 'data'.");
                }

                // Le flux est deja sur les donnees : continuer le balayage
                // reviendrait a perdre leur position.
                break;
            }

            SkipTo(stream, ref position, next, canSeek);
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

        PositionOnData(stream, info);

        byte[] raw = new byte[info.DataLength];
        int read = ReadFully(stream, raw, raw.Length);

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

    /// <summary>
    /// Calcule le SHA-256 de la forme canonique sans la garder en memoire.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Le hachage porte sur les memes octets que
    /// <see cref="Sha256Hex.OfBytesHex(System.ReadOnlySpan{byte})"/> applique
    /// au resultat de <see cref="DecodeToCanonical"/>, mais bloc par bloc. Un
    /// morceau d'une heure en stereo est long de plusieurs centaines de
    /// megaoctets en flottants : les garder tous serait payer deux fois la
    /// memoire pour un resultat qu'on peut obtenir en additionnant.
    /// </para>
    /// <para>
    /// Comme <see cref="DecodeToCanonical"/>, un 'data' tronque donne le hash
    /// de ce qui est reellement lisible, et un echantillon partiel est jete.
    /// </para>
    /// </remarks>
    /// <param name="stream">Flux positionne au debut du fichier.</param>
    /// <param name="info">Resultat de <see cref="ReadInfo(Stream)"/> sur le meme flux.</param>
    /// <param name="sampleCount">Nombre d'echantillons canoniques lus.</param>
    /// <returns>Le SHA-256 de la forme canonique, en hexadecimal minuscule.</returns>
    /// <exception cref="WavFormatException">Le flux n'est pas un WAV lisible.</exception>
    public static string HashCanonicalToHex(Stream stream, WavInfo info, out long sampleCount)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(info);

        PositionOnData(stream, info);

        int bytesPerSample = info.BitsPerSample / 8;
        int blockBytes = CanonicalBlockBytes(bytesPerSample);
        int samplesPerBlock = blockBytes / bytesPerSample;

        byte[] raw = new byte[blockBytes];
        float[] block = new float[samplesPerBlock];
        long total = 0;
        long remaining = info.DataLength;

        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        while (remaining > 0)
        {
            int wanted = (int)Math.Min(raw.Length, remaining);
            int read = ReadFully(stream, raw, wanted);
            if (read <= 0)
            {
                break;
            }

            remaining -= read;

            // Un echantillon partiel ne formerait pas une image complete.
            int count = (read - (read % bytesPerSample)) / bytesPerSample;
            if (count <= 0)
            {
                break;
            }

            for (int i = 0; i < count; i++)
            {
                block[i] = DecodeSample(raw, i * bytesPerSample, info);
            }

            // MemoryMarshal.AsBytes ne copie rien.
            hash.AppendData(MemoryMarshal.AsBytes<float>(block.AsSpan(0, count)));
            total += count;
        }

        sampleCount = total;
        return Sha256Hex.ToHex(hash.GetHashAndReset());
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

    private static void PositionOnData(Stream stream, WavInfo info)
    {
        if (stream.CanSeek)
        {
            stream.Position = info.DataOffset;
            return;
        }

        if (TryGetPosition(stream, out long position) && position == info.DataOffset)
        {
            return;
        }

        // Position inconnue : aucun flux ne peut le dire, et c'est le cas de
        // tout ce qui n'est pas seekable. On ne peut donc pas reprocher au
        // flux d'etre mal positionne — seulement a l'appelant de l'avoir
        // laisse au bon endroit, ce que garantit la lecture d'en-tete qui
        // precede. Refuser ici reviendrait a interdire tout flux d'archive,
        // dont la seule garantie est la position courante.
        if (TryGetPosition(stream, out _))
        {
            throw new WavFormatException(
                "Le flux n'est pas seekable et n'est pas positionne sur le chunk 'data'. " +
                "Rouvrir l'en-tete sur ce flux, ou le copier dans un tampon.");
        }
    }

    private static bool TryGetLength(Stream stream, out long length)
    {
        try
        {
            length = stream.Length;
            return true;
        }
        catch (NotSupportedException)
        {
            length = -1;
            return false;
        }
    }

    private static bool TryGetPosition(Stream stream, out long position)
    {
        try
        {
            position = stream.Position;
            return true;
        }
        catch (NotSupportedException)
        {
            position = -1;
            return false;
        }
    }

    private static void SkipTo(Stream stream, ref long position, long target, bool canSeek)
    {
        if (target <= position)
        {
            return;
        }

        if (canSeek)
        {
            stream.Position = target;
            position = target;
            return;
        }

        // Sans repositionnement, la seule facon d'avancer est de traverser.
        Span<byte> discard = stackalloc byte[4096];
        while (position < target)
        {
            int wanted = (int)Math.Min(discard.Length, target - position);
            int read = stream.Read(discard[..wanted]);
            if (read <= 0)
            {
                break;
            }

            position += read;
        }
    }

    private static int ReadFully(Stream stream, byte[] buffer, int count)
    {
        int read = 0;
        while (read < count)
        {
            int chunk = stream.Read(buffer, read, count - read);
            if (chunk <= 0)
            {
                break;
            }

            read += chunk;
        }

        return read;
    }

    /// <summary>Taille d'un bloc de lecture, multiple de la largeur d'echantillon.</summary>
    private static int CanonicalBlockBytes(int bytesPerSample)
    {
        const int Target = 64 * 1024;
        return (Target / bytesPerSample) * bytesPerSample;
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
