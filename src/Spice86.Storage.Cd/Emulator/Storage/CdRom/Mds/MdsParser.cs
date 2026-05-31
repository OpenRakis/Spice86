using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Spice86.Shared.Emulator.Storage.CdRom.Mds;

/// <summary>
/// Parses an MDS (Alcohol 120%) disc descriptor file. The on-disk layout is the
/// one documented in dosbox-staging's <c>cdrom_mds.h</c> (de-glib'd from
/// cdemu/libmirage). Multi-session discs are truncated to the first session.
/// </summary>
/// <remarks>
/// Layout: header (88 bytes) -> session block at <c>session_block_offset</c> ->
/// <c>num_all_blocks</c> consecutive track blocks (80 bytes each) starting at
/// <c>track_block_offset</c>. Each track block references an extra block (length)
/// and a footer (filename pointer). Non-track entries (point &lt; 1 or &gt; 99)
/// are skipped.
/// </remarks>
public sealed class MdsParser {
    private const int HeaderSize = 88;
    private const int SessionBlockSize = 24;
    private const int TrackBlockSize = 80;
    private const int ExtraBlockSize = 8;
    private const int FooterSize = 16;
    private const int MinValidPoint = 1;
    private const int MaxValidPoint = 99;

    /// <summary>ASCII signature bytes of a valid MDS header: <c>"MEDIA DESCRIPTOR"</c>.</summary>
    public static readonly byte[] Signature = Encoding.ASCII.GetBytes("MEDIA DESCRIPTOR");

    /// <summary>Parses the MDS file at the given path.</summary>
    /// <param name="mdsFilePath">Path to the .mds file.</param>
    /// <returns>The parsed disc descriptor.</returns>
    /// <exception cref="ArgumentNullException">Thrown when the path is null.</exception>
    /// <exception cref="FileNotFoundException">Thrown when the path does not exist.</exception>
    /// <exception cref="InvalidDataException">Thrown when the file is not a valid MDS file.</exception>
    public MdsDiscDescriptor ParseFile(string mdsFilePath) {
        ArgumentNullException.ThrowIfNull(mdsFilePath);
        using FileStream stream = new FileStream(mdsFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Parse(stream);
    }

    /// <summary>Parses an MDS file from an open seekable stream.</summary>
    /// <param name="stream">Seekable stream positioned anywhere; the parser seeks to absolute offsets.</param>
    /// <returns>The parsed disc descriptor.</returns>
    /// <exception cref="ArgumentNullException">Thrown when the stream is null.</exception>
    /// <exception cref="InvalidDataException">Thrown when the stream does not contain a valid MDS layout.</exception>
    public MdsDiscDescriptor Parse(Stream stream) {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanSeek) {
            throw new InvalidDataException("MDS parser requires a seekable stream.");
        }

        Span<byte> headerBuffer = stackalloc byte[HeaderSize];
        ReadAt(stream, 0L, headerBuffer);
        ValidateHeader(headerBuffer, out uint sessionBlockOffset);

        Span<byte> sessionBuffer = stackalloc byte[SessionBlockSize];
        ReadAt(stream, sessionBlockOffset, sessionBuffer);
        ValidateSessionBlock(sessionBuffer, out int numAllBlocks, out uint trackBlockOffset);

        List<MdsTrack> tracks = new List<MdsTrack>(numAllBlocks);
        int previousTrackNumber = 0;
        int previousEndSector = 0;
        byte[] trackBuffer = new byte[TrackBlockSize];
        byte[] extraBuffer = new byte[ExtraBlockSize];
        byte[] footerBuffer = new byte[FooterSize];
        for (int i = 0; i < numAllBlocks; i++) {
            long blockPos = trackBlockOffset + (long)i * TrackBlockSize;
            ReadAt(stream, blockPos, trackBuffer);

            byte point = trackBuffer[4];
            if (point is < MinValidPoint or > MaxValidPoint) {
                continue;
            }

            byte modeRaw = trackBuffer[0];
            byte subchannelRaw = trackBuffer[1];
            uint extraOffset = BinaryPrimitives.ReadUInt32LittleEndian(trackBuffer.AsSpan(12, 4));
            ushort sectorSize = BinaryPrimitives.ReadUInt16LittleEndian(trackBuffer.AsSpan(16, 2));
            uint startSector = BinaryPrimitives.ReadUInt32LittleEndian(trackBuffer.AsSpan(36, 4));
            ulong startOffset = BinaryPrimitives.ReadUInt64LittleEndian(trackBuffer.AsSpan(40, 8));
            uint numberOfFiles = BinaryPrimitives.ReadUInt32LittleEndian(trackBuffer.AsSpan(48, 4));
            uint footerOffset = BinaryPrimitives.ReadUInt32LittleEndian(trackBuffer.AsSpan(52, 4));

            MdsTrackMode mode = DecodeTrackMode(modeRaw);
            int subchannelSize = DecodeSubchannelSize(subchannelRaw);
            if (subchannelSize >= sectorSize) {
                throw new InvalidDataException($"Invalid sector/subchannel size. Sector size: {sectorSize} Subchannel size: {subchannelSize}");
            }
            if (numberOfFiles != 1) {
                throw new InvalidDataException($"Track {point} has {numberOfFiles} files; only single-file tracks are supported.");
            }
            if (footerOffset == 0 || extraOffset == 0) {
                throw new InvalidDataException("Invalid MDS file: track block has missing extra or footer offset.");
            }

            Span<byte> extraSpan = extraBuffer;
            ReadAt(stream, extraOffset, extraSpan);
            uint lengthSectors = BinaryPrimitives.ReadUInt32LittleEndian(extraSpan.Slice(4, 4));

            Span<byte> footerSpan = footerBuffer;
            ReadAt(stream, footerOffset, footerSpan);
            uint filenameOffset = BinaryPrimitives.ReadUInt32LittleEndian(footerSpan.Slice(0, 4));
            uint widecharFlag = BinaryPrimitives.ReadUInt32LittleEndian(footerSpan.Slice(4, 4));
            if (filenameOffset == 0) {
                throw new InvalidDataException("Invalid MDS file: footer has zero filename offset.");
            }

            if (point != previousTrackNumber + 1 || startSector < previousEndSector) {
                throw new InvalidDataException("Non-contiguous track found in MDS file.");
            }

            string mdfFilename = ReadFilename(stream, filenameOffset, widecharFlag != 0);

            MdsTrack track = new MdsTrack(
                number: point,
                mode: mode,
                sectorSize: sectorSize,
                subchannelSize: subchannelSize,
                startSector: (int)startSector,
                skipBytes: (long)startOffset,
                lengthSectors: (int)lengthSectors,
                mdfFilename: mdfFilename);
            tracks.Add(track);
            previousTrackNumber = point;
            previousEndSector = (int)(startSector + lengthSectors);
        }

        if (tracks.Count == 0) {
            throw new InvalidDataException("Failed to find any tracks in MDS file.");
        }

        return new MdsDiscDescriptor(tracks);
    }

    private static void ValidateHeader(ReadOnlySpan<byte> header, out uint sessionBlockOffset) {
        if (!header.Slice(0, Signature.Length).SequenceEqual(Signature)) {
            throw new InvalidDataException("Not an MDS file: signature mismatch.");
        }
        byte majorVersion = header[16];
        if (majorVersion != 1) {
            throw new InvalidDataException($"Unsupported MDS major version: {majorVersion}.");
        }
        ushort numSessions = BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(20, 2));
        if (numSessions == 0) {
            throw new InvalidDataException("Invalid MDS file: zero sessions.");
        }
        sessionBlockOffset = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(80, 4));
        if (sessionBlockOffset == 0) {
            throw new InvalidDataException("Invalid MDS file: zero session block offset.");
        }
    }

    private static void ValidateSessionBlock(ReadOnlySpan<byte> block, out int numAllBlocks, out uint trackBlockOffset) {
        numAllBlocks = block[10];
        if (numAllBlocks == 0) {
            throw new InvalidDataException("Invalid MDS file: session block reports zero track blocks.");
        }
        trackBlockOffset = BinaryPrimitives.ReadUInt32LittleEndian(block.Slice(20, 4));
        if (trackBlockOffset == 0) {
            throw new InvalidDataException("Invalid MDS file: session block has zero track block offset.");
        }
    }

    private static MdsTrackMode DecodeTrackMode(byte rawMode) {
        int mode = rawMode & 0x07;
        if (mode == 1) {
            return MdsTrackMode.Audio;
        }
        if (mode == 2) {
            return MdsTrackMode.Mode1Data;
        }
        if (mode is 0 or 3 or 7) {
            return MdsTrackMode.Mode2Data;
        }
        throw new InvalidDataException($"Unsupported MDS track mode: {mode}.");
    }

    private static int DecodeSubchannelSize(byte rawSubchannel) {
        if (rawSubchannel == 0) {
            return 0;
        }
        if (rawSubchannel == 8) {
            return 96;
        }
        return 0;
    }

    private static string ReadFilename(Stream stream, uint offset, bool isWideChar) {
        stream.Seek(offset, SeekOrigin.Begin);
        if (isWideChar) {
            StringBuilder builder = new StringBuilder();
            byte[] codeUnit = new byte[2];
            while (true) {
                if (stream.Read(codeUnit, 0, 2) != 2) {
                    throw new InvalidDataException("Truncated wide MDF filename in MDS footer.");
                }
                ushort value = BinaryPrimitives.ReadUInt16LittleEndian(codeUnit);
                if (value == 0) {
                    return builder.ToString();
                }
                builder.Append((char)value);
            }
        }
        StringBuilder asciiBuilder = new StringBuilder();
        while (true) {
            int b = stream.ReadByte();
            if (b < 0) {
                throw new InvalidDataException("Truncated MDF filename in MDS footer.");
            }
            if (b == 0) {
                return asciiBuilder.ToString();
            }
            asciiBuilder.Append((char)b);
        }
    }

    private static void ReadAt(Stream stream, long offset, Span<byte> destination) {
        stream.Seek(offset, SeekOrigin.Begin);
        int total = 0;
        while (total < destination.Length) {
            int read = stream.Read(destination.Slice(total));
            if (read <= 0) {
                throw new InvalidDataException("Unexpected end of MDS stream.");
            }
            total += read;
        }
    }
}
