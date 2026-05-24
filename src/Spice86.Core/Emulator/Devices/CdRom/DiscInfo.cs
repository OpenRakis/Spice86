namespace Spice86.Core.Emulator.Devices.CdRom;

/// <summary>Contains summary information about a CD-ROM disc.</summary>
public sealed class DiscInfo {
    /// <summary>Gets the number of the first track on the disc (usually 1).</summary>
    public int FirstTrack { get; }

    /// <summary>Gets the number of the last track on the disc.</summary>
    public int LastTrack { get; }

    /// <summary>Gets the total number of sectors on the disc.</summary>
    public int TotalSectors { get; }

    /// <summary>Gets the logical block address of the lead-out track.</summary>
    public int LeadOutLba { get; }

    /// <summary>Initialises a new <see cref="DiscInfo"/> with all required disc attributes.</summary>
    /// <param name="firstTrack">The first track number.</param>
    /// <param name="lastTrack">The last track number.</param>
    /// <param name="totalSectors">The total sector count.</param>
    /// <param name="leadOutLba">The LBA of the lead-out track.</param>
    public DiscInfo(int firstTrack, int lastTrack, int totalSectors, int leadOutLba) {
        FirstTrack = firstTrack;
        LastTrack = lastTrack;
        TotalSectors = totalSectors;
        LeadOutLba = leadOutLba;
    }
}
