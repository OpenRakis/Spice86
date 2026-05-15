namespace Spice86.Shared.Emulator.Storage.CdRom.Mds;

/// <summary>
/// Parsed descriptor for a single MDS track block. Immutable. Mirrors the
/// fields dosbox-staging's <c>LoadMdsFile</c> reads from each
/// <c>MdsTrackBlock</c> + its associated extra block + footer.
/// </summary>
public sealed class MdsTrack
{
    /// <summary>Initialises a new <see cref="MdsTrack"/>.</summary>
    /// <param name="number">Track number (1..99) — the <c>point</c> field from the MDS track block.</param>
    /// <param name="mode">Decoded high-level mode.</param>
    /// <param name="sectorSize">Bytes per sector (typically 2048 for data, 2352 for audio).</param>
    /// <param name="subchannelSize">Subchannel bytes appended to each sector (0 or 96).</param>
    /// <param name="startSector">Absolute starting LBA of this track on the disc.</param>
    /// <param name="skipBytes">Byte offset within the backing MDF file where this track's first sector resides.</param>
    /// <param name="lengthSectors">Track length in sectors (from the associated extra block).</param>
    /// <param name="mdfFilename">Filename of the MDF that contains this track's data (relative or fully qualified).</param>
    public MdsTrack(
        int number,
        MdsTrackMode mode,
        int sectorSize,
        int subchannelSize,
        int startSector,
        long skipBytes,
        int lengthSectors,
        string mdfFilename)
    {
        Number = number;
        Mode = mode;
        SectorSize = sectorSize;
        SubchannelSize = subchannelSize;
        StartSector = startSector;
        SkipBytes = skipBytes;
        LengthSectors = lengthSectors;
        MdfFilename = mdfFilename;
    }

    /// <summary>Gets the track number (the MDS <c>point</c> field, 1..99).</summary>
    public int Number { get; }

    /// <summary>Gets the decoded high-level mode.</summary>
    public MdsTrackMode Mode { get; }

    /// <summary>Gets the bytes per sector for this track.</summary>
    public int SectorSize { get; }

    /// <summary>Gets the per-sector subchannel byte count (0 or 96).</summary>
    public int SubchannelSize { get; }

    /// <summary>Gets the absolute starting LBA on the disc.</summary>
    public int StartSector { get; }

    /// <summary>Gets the byte offset within the MDF where this track begins.</summary>
    public long SkipBytes { get; }

    /// <summary>Gets the length of this track in sectors.</summary>
    public int LengthSectors { get; }

    /// <summary>Gets the MDF filename referenced by this track's footer.</summary>
    public string MdfFilename { get; }
}
