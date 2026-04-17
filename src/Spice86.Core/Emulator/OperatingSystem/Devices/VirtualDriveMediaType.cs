namespace Spice86.Core.Emulator.OperatingSystem.Devices;

/// <summary>
/// Enumeration of virtual drive media types supported by the emulator.
/// </summary>
internal enum VirtualDriveMediaType {
    /// <summary>Fixed hard disk drive.</summary>
    Fixed = 0,

    /// <summary>3.5-inch or 5.25-inch floppy disk drive (future support).</summary>
    Floppy = 1,

    /// <summary>CD-ROM drive with MSCDEX support (future support).</summary>
    CdRom = 2,

    /// <summary>Unknown or unspecified drive type.</summary>
    Unknown = 99,
}
