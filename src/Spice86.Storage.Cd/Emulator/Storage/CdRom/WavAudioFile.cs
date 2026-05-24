namespace Spice86.Shared.Emulator.Storage.CdRom;

using System.IO;

/// <summary>
/// Parses the RIFF/WAVE header of a PCM audio file referenced by a CUE
/// sheet and exposes the location of the PCM payload within the file.
/// Only CDDA-compliant streams (44.1 kHz, stereo, 16-bit PCM) are
/// supported; non-conforming files are rejected so callers can either
/// transcode or fall back to a different pipeline.
/// </summary>
public sealed class WavAudioFile
{
    private const int RiffHeaderSize = 12;
    private const ushort PcmFormatTag = 1;
    private const int CddaSampleRate = 44100;
    private const int CddaChannelCount = 2;
    private const int CddaBitsPerSample = 16;

    /// <summary>Opens the WAV file at <paramref name="filePath"/> and parses its header.</summary>
    /// <param name="filePath">Path to a CDDA-compliant <c>.wav</c> file.</param>
    /// <exception cref="InvalidDataException">
    /// Thrown when the file is not a valid RIFF/WAVE stream or is not
    /// CDDA-compliant.
    /// </exception>
    /// <exception cref="IOException">Thrown when the file cannot be opened.</exception>
    public WavAudioFile(string filePath)
    {
        FilePath = filePath;
        ParseHeader();
    }

    /// <summary>Gets the path to the WAV file.</summary>
    public string FilePath { get; }

    /// <summary>Gets the byte offset within the file where the PCM payload begins.</summary>
    public long PcmDataOffset { get; private set; }

    /// <summary>Gets the length of the PCM payload, in bytes.</summary>
    public long PcmDataLength { get; private set; }

    /// <summary>Gets the sample rate (always 44100 for CDDA).</summary>
    public int SampleRate { get; private set; }

    /// <summary>Gets the channel count (always 2 for CDDA).</summary>
    public int ChannelCount { get; private set; }

    /// <summary>Gets the bits-per-sample (always 16 for CDDA).</summary>
    public int BitsPerSample { get; private set; }

    private void ParseHeader()
    {
        using FileStream stream = File.OpenRead(FilePath);
        using BinaryReader reader = new(stream);

        if (stream.Length < RiffHeaderSize)
        {
            throw new InvalidDataException("WAV file is too small to contain a RIFF header.");
        }

        string riff = new(reader.ReadChars(4));
        if (riff != "RIFF")
        {
            throw new InvalidDataException($"Expected 'RIFF' signature, got '{riff}'.");
        }
        reader.ReadInt32(); // chunk size (ignored, validated via stream length)
        string wave = new(reader.ReadChars(4));
        if (wave != "WAVE")
        {
            throw new InvalidDataException($"Expected 'WAVE' form, got '{wave}'.");
        }

        bool fmtParsed = false;
        bool dataLocated = false;

        while (stream.Position + 8 <= stream.Length)
        {
            string chunkId = new(reader.ReadChars(4));
            int chunkSize = reader.ReadInt32();
            long chunkStart = stream.Position;

            if (chunkId == "fmt ")
            {
                if (chunkSize < 16)
                {
                    throw new InvalidDataException("'fmt ' chunk is shorter than 16 bytes.");
                }
                ushort formatTag = reader.ReadUInt16();
                ushort channels = reader.ReadUInt16();
                int sampleRate = reader.ReadInt32();
                reader.ReadInt32(); // byte rate
                reader.ReadUInt16(); // block align
                ushort bitsPerSample = reader.ReadUInt16();

                if (formatTag != PcmFormatTag)
                {
                    throw new InvalidDataException($"Only PCM WAV files are supported (formatTag={formatTag}).");
                }
                if (sampleRate != CddaSampleRate || channels != CddaChannelCount || bitsPerSample != CddaBitsPerSample)
                {
                    throw new InvalidDataException(
                        $"WAV file is not CDDA-compliant (got {sampleRate} Hz, {channels} ch, {bitsPerSample}-bit; expected 44100 Hz, 2 ch, 16-bit).");
                }
                SampleRate = sampleRate;
                ChannelCount = channels;
                BitsPerSample = bitsPerSample;
                fmtParsed = true;
                stream.Position = chunkStart + chunkSize;
            }
            else if (chunkId == "data")
            {
                if (!fmtParsed)
                {
                    throw new InvalidDataException("'data' chunk encountered before 'fmt ' chunk.");
                }
                PcmDataOffset = chunkStart;
                PcmDataLength = chunkSize;
                dataLocated = true;
                break;
            }
            else
            {
                stream.Position = chunkStart + chunkSize;
            }
            if ((chunkSize & 1) == 1 && stream.Position < stream.Length)
            {
                // RIFF chunks are word-aligned.
                stream.Position++;
            }
        }

        if (!fmtParsed)
        {
            throw new InvalidDataException("WAV file is missing the 'fmt ' chunk.");
        }
        if (!dataLocated)
        {
            throw new InvalidDataException("WAV file is missing the 'data' chunk.");
        }
    }
}
