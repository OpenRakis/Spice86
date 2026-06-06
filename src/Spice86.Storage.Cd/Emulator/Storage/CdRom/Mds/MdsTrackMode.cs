namespace Spice86.Shared.Emulator.Storage.CdRom.Mds;

/// <summary>
/// High-level track mode parsed from an MDS track block.
/// </summary>
public enum MdsTrackMode
{
    /// <summary>Red Book audio track (raw 2352 bytes/sector, attribute 0x00).</summary>
    Audio = 0,

    /// <summary>Mode 1 data track (attribute 0x40, mode2 = false).</summary>
    Mode1Data = 1,

    /// <summary>Mode 2 data track (attribute 0x40, mode2 = true).</summary>
    Mode2Data = 2
}
