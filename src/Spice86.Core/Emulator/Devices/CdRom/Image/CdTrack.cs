namespace Spice86.Core.Emulator.Devices.CdRom.Image;

/// <summary>Describes a single track on a CD-ROM image.</summary>
public sealed class CdTrack {
    /// <summary>Gets the track number (1–99).</summary>
    public int Number { get; }

    /// <summary>Gets the starting logical block address of this track.</summary>
    public int StartLba { get; }

    /// <summary>Gets the number of sectors in this track.</summary>
    public int LengthSectors { get; }

    /// <summary>Gets the number of bytes per sector (2048 or 2352).</summary>
    public int SectorSize { get; }

    /// <summary>Gets the sector encoding mode of this track.</summary>
    public CdSectorMode Mode { get; }

    /// <summary>Gets a value indicating whether this track contains audio data.</summary>
    public bool IsAudio { get; }

    /// <summary>Gets the pregap length in sectors (usually 150 for the first track).</summary>
    public int Pregap { get; }

    /// <summary>Gets the postgap length in sectors.</summary>
    public int Postgap { get; }

    /// <summary>Gets the data source that backs this track.</summary>
    public IDataSource Source { get; }

    /// <summary>Gets the byte offset into the source file where this track's data begins.</summary>
    public long FileOffset { get; }

    /// <summary>Initialises a new <see cref="CdTrack"/> with all required attributes.</summary>
    public CdTrack(
        int number,
        int startLba,
        int lengthSectors,
        int sectorSize,
        CdSectorMode mode,
        bool isAudio,
        int pregap,
        int postgap,
        IDataSource source,
        long fileOffset) {
        Number = number;
        StartLba = startLba;
        LengthSectors = lengthSectors;
        SectorSize = sectorSize;
        Mode = mode;
        IsAudio = isAudio;
        Pregap = pregap;
        Postgap = postgap;
        Source = source;
        FileOffset = fileOffset;
    }
}
