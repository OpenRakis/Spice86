namespace Spice86.Shared.Emulator.Storage.CdRom;

/// <summary>
/// Immutable, file-format-agnostic resolved geometry for a single CUE track,
/// produced by <see cref="CueFrameMapper"/>. Captures both the absolute MSF
/// frame positions from the CUE sheet and the derived TOC values (logical LBA,
/// sector length, file byte offset) so callers do not need to repeat the
/// Red Book conversion arithmetic.
/// </summary>
public sealed class CueTrackLayout
{
    /// <summary>Gets the 1-based CUE track number.</summary>
    public int TrackNumber { get; }

    /// <summary>Gets the resolved path of the file backing this track.</summary>
    public string FileName { get; }

    /// <summary>Gets the CUE FILE type token (e.g. <see cref="CueFileType.Binary"/>, <see cref="CueFileType.Wave"/>).</summary>
    public CueFileType FileType { get; }

    /// <summary>Gets the CUE TRACK mode token (e.g. <c>MODE1/2048</c>, <c>AUDIO</c>).</summary>
    public string TrackMode { get; }

    /// <summary>Gets the absolute INDEX 01 position in CUE frames (75 per second).</summary>
    public int Index01Frames { get; }

    /// <summary>
    /// Gets the absolute INDEX 00 (in-file pregap) position in CUE frames,
    /// or <see langword="null"/> when the track does not declare INDEX 00.
    /// </summary>
    public int? Index00Frames { get; }

    /// <summary>Gets the logical LBA at which this track's INDEX 01 maps (Red Book offset already subtracted).</summary>
    public int StartLba { get; }

    /// <summary>Gets the number of sectors this track occupies in the TOC.</summary>
    public int LengthSectors { get; }

    /// <summary>Gets the byte offset into the backing file where this track's INDEX 01 data begins.</summary>
    public long FileByteOffset { get; }

    /// <summary>Gets the sector size in bytes derived from <see cref="TrackMode"/>.</summary>
    public int SectorSize { get; }

    /// <summary>Gets the implicit pregap length (from the <c>PREGAP</c> directive) in frames.</summary>
    public int PregapFrames { get; }

    /// <summary>Gets the postgap length (from the <c>POSTGAP</c> directive) in frames.</summary>
    public int PostgapFrames { get; }

    /// <summary>Initialises a new <see cref="CueTrackLayout"/> with all resolved values.</summary>
    public CueTrackLayout(
        int trackNumber,
        string fileName,
        CueFileType fileType,
        string trackMode,
        int index01Frames,
        int? index00Frames,
        int startLba,
        int lengthSectors,
        long fileByteOffset,
        int sectorSize,
        int pregapFrames,
        int postgapFrames)
    {
        TrackNumber = trackNumber;
        FileName = fileName;
        FileType = fileType;
        TrackMode = trackMode;
        Index01Frames = index01Frames;
        Index00Frames = index00Frames;
        StartLba = startLba;
        LengthSectors = lengthSectors;
        FileByteOffset = fileByteOffset;
        SectorSize = sectorSize;
        PregapFrames = pregapFrames;
        PostgapFrames = postgapFrames;
    }
}
