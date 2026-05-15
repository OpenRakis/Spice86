namespace Spice86.Storage.Tests.CdRom.Mds;

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

/// <summary>
/// Fluent builder for synthetic MDS+MDF pairs used in storage tests. Emits a
/// single-session MDS that mirrors the byte layout in dosbox-staging's
/// <c>cdrom_mds.h</c> (de-glib'd from cdemu/libmirage), plus a paired raw MDF
/// data file whose first sector of each data track is a minimally-valid ISO
/// 9660 Primary Volume Descriptor so that <see cref="Spice86.Shared.Emulator.Storage.CdRom.MdsImage"/>'s
/// PVD parser succeeds.
/// </summary>
/// <remarks>
/// MDS layout produced by <see cref="WriteToDisk"/>:
/// <list type="number">
///   <item><description>Bytes 0..88: header.</description></item>
///   <item><description>Bytes 88..112: session block.</description></item>
///   <item><description>Bytes 112..(112 + 80*N): track blocks.</description></item>
///   <item><description>Per track: an 8-byte extra block + a 16-byte footer + an
///   ASCII (NUL-terminated) MDF filename. Track 1 may be configured to also emit
///   a single non-track marker block before the data tracks (point = 0xA2 lead-out)
///   to exercise the skip-non-track-blocks path.</description></item>
/// </list>
/// </remarks>
internal sealed class MdsImageBuilder
{
    private const int HeaderSize = 88;
    private const int SessionBlockSize = 24;
    private const int TrackBlockSize = 80;
    private const int ExtraBlockSize = 8;
    private const int FooterSize = 16;

    private readonly List<TrackSpec> _tracks = new();
    private string _mdfRelativeName = "test.mdf";
    private bool _invalidSignature;
    private byte _majorVersion = 1;
    private ushort _numSessions = 1;
    private bool _writeLeadOutMarker;
    private bool _emitWideFilename;

    /// <summary>Adds a data track (Mode 1, 2048 bytes per sector).</summary>
    public MdsImageBuilder WithDataTrack(int trackNumber, int startSector, int lengthSectors)
    {
        _tracks.Add(new TrackSpec(Point: (byte)trackNumber, ModeRaw: 2, SectorSize: 2048, Subchannel: 0, StartSector: startSector, LengthSectors: lengthSectors));
        return this;
    }

    /// <summary>Adds an audio track (raw 2352 bytes per sector).</summary>
    public MdsImageBuilder WithAudioTrack(int trackNumber, int startSector, int lengthSectors)
    {
        _tracks.Add(new TrackSpec(Point: (byte)trackNumber, ModeRaw: 1, SectorSize: 2352, Subchannel: 0, StartSector: startSector, LengthSectors: lengthSectors));
        return this;
    }

    /// <summary>Adds a lead-out marker (point = 0xA2) before the data tracks to exercise non-track-skipping.</summary>
    public MdsImageBuilder WithLeadOutMarker()
    {
        _writeLeadOutMarker = true;
        return this;
    }

    /// <summary>Forces an invalid signature so the parser must reject the file.</summary>
    public MdsImageBuilder WithInvalidSignature()
    {
        _invalidSignature = true;
        return this;
    }

    /// <summary>Sets the major version byte (1 is valid; any other value must be rejected).</summary>
    public MdsImageBuilder WithMajorVersion(byte version)
    {
        _majorVersion = version;
        return this;
    }

    /// <summary>Sets the session-count header field; 0 must be rejected.</summary>
    public MdsImageBuilder WithSessionCount(ushort count)
    {
        _numSessions = count;
        return this;
    }

    /// <summary>Sets the MDF filename written into each footer (defaults to <c>test.mdf</c>).</summary>
    public MdsImageBuilder WithMdfFilename(string name)
    {
        _mdfRelativeName = name;
        return this;
    }

    /// <summary>Emits the filename as UTF-16 (LE) and flips the widechar flag.</summary>
    public MdsImageBuilder WithWideFilename()
    {
        _emitWideFilename = true;
        return this;
    }

    /// <summary>
    /// Writes the MDS file to <paramref name="mdsPath"/>. When data tracks are
    /// present, also writes a paired MDF beside it whose first data sector is
    /// a minimally-valid ISO 9660 PVD so that the PVD parser succeeds.
    /// </summary>
    public void WriteToDisk(string mdsPath)
    {
        byte[] mdsBytes = BuildMds();
        File.WriteAllBytes(mdsPath, mdsBytes);

        string mdfDirectory = Path.GetDirectoryName(Path.GetFullPath(mdsPath))!;
        string mdfPath = Path.Combine(mdfDirectory, _mdfRelativeName);
        byte[] mdfBytes = BuildMdf();
        File.WriteAllBytes(mdfPath, mdfBytes);
    }

    private byte[] BuildMds()
    {
        int totalBlocks = _tracks.Count + (_writeLeadOutMarker ? 1 : 0);
        int trackBlockOffset = HeaderSize + SessionBlockSize;
        int dataAreaOffset = trackBlockOffset + (TrackBlockSize * totalBlocks);
        byte[] filenameBytes = EncodeFilename(_mdfRelativeName);
        int perTrackAuxSize = ExtraBlockSize + FooterSize + filenameBytes.Length;
        int totalSize = dataAreaOffset + (perTrackAuxSize * _tracks.Count);
        byte[] image = new byte[totalSize];

        WriteHeader(image, trackBlockOffsetPtr: HeaderSize);
        WriteSessionBlock(image.AsSpan(HeaderSize, SessionBlockSize), trackBlockOffset, (byte)totalBlocks, (byte)_tracks.Count);

        int trackIndex = 0;
        if (_writeLeadOutMarker)
        {
            WriteNonTrackMarker(image.AsSpan(trackBlockOffset + (trackIndex * TrackBlockSize), TrackBlockSize));
            trackIndex++;
        }

        for (int i = 0; i < _tracks.Count; i++, trackIndex++)
        {
            int extraOffset = dataAreaOffset + (i * perTrackAuxSize);
            int footerOffset = extraOffset + ExtraBlockSize;
            int filenameOffset = footerOffset + FooterSize;

            WriteTrackBlock(image.AsSpan(trackBlockOffset + (trackIndex * TrackBlockSize), TrackBlockSize), _tracks[i], extraOffset, footerOffset);
            WriteExtraBlock(image.AsSpan(extraOffset, ExtraBlockSize), _tracks[i].LengthSectors);
            WriteFooter(image.AsSpan(footerOffset, FooterSize), filenameOffset);
            Buffer.BlockCopy(filenameBytes, 0, image, filenameOffset, filenameBytes.Length);
        }

        return image;
    }

    private void WriteHeader(byte[] image, int trackBlockOffsetPtr)
    {
        if (_invalidSignature)
        {
            byte[] junk = Encoding.ASCII.GetBytes("NOT AN MDS FILE!");
            Buffer.BlockCopy(junk, 0, image, 0, junk.Length);
        }
        else
        {
            byte[] sig = Encoding.ASCII.GetBytes("MEDIA DESCRIPTOR");
            Buffer.BlockCopy(sig, 0, image, 0, sig.Length);
        }
        image[16] = _majorVersion;
        image[17] = 3; // minor version (irrelevant)
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(20, 2), _numSessions);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(80, 4), (uint)trackBlockOffsetPtr);
    }

    private static void WriteSessionBlock(Span<byte> block, int trackBlockOffset, byte numAllBlocks, byte numTrackBlocks)
    {
        BinaryPrimitives.WriteInt16LittleEndian(block.Slice(8, 2), 1); // session number
        block[10] = numAllBlocks;
        block[11] = (byte)(numAllBlocks - numTrackBlocks);
        BinaryPrimitives.WriteUInt16LittleEndian(block.Slice(12, 2), 1); // first track
        BinaryPrimitives.WriteUInt16LittleEndian(block.Slice(14, 2), numTrackBlocks); // last track
        BinaryPrimitives.WriteUInt32LittleEndian(block.Slice(20, 4), (uint)trackBlockOffset);
    }

    private static void WriteNonTrackMarker(Span<byte> block)
    {
        block[0] = 2; // arbitrary mode
        block[4] = 0xA2; // lead-out point
        BinaryPrimitives.WriteUInt16LittleEndian(block.Slice(16, 2), 2048);
    }

    private static void WriteTrackBlock(Span<byte> block, TrackSpec spec, int extraOffset, int footerOffset)
    {
        block[0] = spec.ModeRaw;
        block[1] = spec.Subchannel;
        block[2] = 0;
        block[3] = spec.Point;
        block[4] = spec.Point;
        BinaryPrimitives.WriteUInt32LittleEndian(block.Slice(12, 4), (uint)extraOffset);
        BinaryPrimitives.WriteUInt16LittleEndian(block.Slice(16, 2), spec.SectorSize);
        BinaryPrimitives.WriteUInt32LittleEndian(block.Slice(36, 4), (uint)spec.StartSector);
        BinaryPrimitives.WriteUInt64LittleEndian(block.Slice(40, 8), (ulong)((long)spec.StartSector * spec.SectorSize));
        BinaryPrimitives.WriteUInt32LittleEndian(block.Slice(48, 4), 1u); // number of files
        BinaryPrimitives.WriteUInt32LittleEndian(block.Slice(52, 4), (uint)footerOffset);
    }

    private static void WriteExtraBlock(Span<byte> block, int lengthSectors)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(block.Slice(0, 4), 0u); // pregap
        BinaryPrimitives.WriteUInt32LittleEndian(block.Slice(4, 4), (uint)lengthSectors);
    }

    private void WriteFooter(Span<byte> block, int filenameOffset)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(block.Slice(0, 4), (uint)filenameOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(block.Slice(4, 4), _emitWideFilename ? 1u : 0u);
    }

    private byte[] EncodeFilename(string name)
    {
        if (_emitWideFilename)
        {
            byte[] body = Encoding.Unicode.GetBytes(name);
            byte[] result = new byte[body.Length + 2];
            Buffer.BlockCopy(body, 0, result, 0, body.Length);
            return result;
        }
        byte[] ascii = Encoding.ASCII.GetBytes(name);
        byte[] terminated = new byte[ascii.Length + 1];
        Buffer.BlockCopy(ascii, 0, terminated, 0, ascii.Length);
        return terminated;
    }

    private byte[] BuildMdf()
    {
        long endByte = 0;
        foreach (TrackSpec t in _tracks)
        {
            long e = (long)(t.StartSector + t.LengthSectors) * t.SectorSize;
            if (e > endByte)
            {
                endByte = e;
            }
        }
        if (endByte == 0)
        {
            return Array.Empty<byte>();
        }
        byte[] mdf = new byte[endByte];

        // For the first data track, lay down a valid ISO 9660 PVD at the appropriate offset.
        foreach (TrackSpec t in _tracks)
        {
            if (t.ModeRaw == 2)
            {
                long pvdOffset = ((long)t.StartSector * t.SectorSize) + 16L * t.SectorSize;
                if (pvdOffset + 256 <= mdf.Length)
                {
                    Span<byte> pvd = mdf.AsSpan((int)pvdOffset, t.SectorSize);
                    pvd[0] = 0x01;
                    byte[] cd001 = Encoding.ASCII.GetBytes("CD001");
                    cd001.AsSpan().CopyTo(pvd.Slice(1, 5));
                    pvd[6] = 1;
                    Encoding.ASCII.GetBytes("TESTVOL ".PadRight(32, ' ')).AsSpan().CopyTo(pvd.Slice(40, 32));
                    BinaryPrimitives.WriteInt32LittleEndian(pvd.Slice(80, 4), 100);
                    BinaryPrimitives.WriteUInt16LittleEndian(pvd.Slice(128, 2), 2048);
                    // Root dir record: 34 byte length, extent at LBA 20, length 2048.
                    pvd[156] = 34;
                    BinaryPrimitives.WriteInt32LittleEndian(pvd.Slice(158, 4), 20);
                    BinaryPrimitives.WriteInt32LittleEndian(pvd.Slice(166, 4), 2048);
                }
                break;
            }
        }
        return mdf;
    }

    private readonly record struct TrackSpec(byte Point, byte ModeRaw, ushort SectorSize, byte Subchannel, int StartSector, int LengthSectors);
}
