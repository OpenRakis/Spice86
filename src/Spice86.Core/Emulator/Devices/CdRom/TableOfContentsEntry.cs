namespace Spice86.Core.Emulator.Devices.CdRom;

/// <summary>Represents a single entry in the CD-ROM table of contents.</summary>
public sealed class TableOfContentsEntry {
    /// <summary>Gets the track number. The lead-out track uses 0xAA.</summary>
    public int TrackNumber { get; }

    /// <summary>Gets the starting logical block address of this track.</summary>
    public int Lba { get; }

    /// <summary>Gets a value indicating whether this track contains audio data.</summary>
    public bool IsAudio { get; }

    /// <summary>Gets the 4-bit control nibble per Red Book: 0 for audio, 4 for data.</summary>
    public byte Control { get; }

    /// <summary>Gets the ADR nibble. A value of 1 means the sub-channel contains current position in LBA.</summary>
    public byte Adr { get; }

    /// <summary>Initialises a new <see cref="TableOfContentsEntry"/> with all required attributes.</summary>
    /// <param name="trackNumber">The track number (1–99, or 0xAA for lead-out).</param>
    /// <param name="lba">The starting logical block address of the track.</param>
    /// <param name="isAudio">Whether the track is an audio track.</param>
    /// <param name="control">The 4-bit control nibble (0 = audio, 4 = data).</param>
    /// <param name="adr">The ADR nibble.</param>
    public TableOfContentsEntry(int trackNumber, int lba, bool isAudio, byte control, byte adr) {
        TrackNumber = trackNumber;
        Lba = lba;
        IsAudio = isAudio;
        Control = control;
        Adr = adr;
    }
}
