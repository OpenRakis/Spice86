namespace Spice86.Shared.Emulator.Storage;

/// <summary>
/// The type of media or drive attached to a DOS drive letter.
/// </summary>
public enum DosVirtualDriveType {
    /// <summary>Fixed hard disk drive.</summary>
    Fixed = 0,

    /// <summary>Floppy disk drive (A: or B:).</summary>
    Floppy = 1,

    /// <summary>CD-ROM drive (read-only optical media).</summary>
    CdRom = 2,

    /// <summary>In-memory virtual drive (e.g. Z:).</summary>
    Memory = 3,
}
