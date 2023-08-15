namespace Spice86.Core.Emulator.OperatingSystem.Enums;
/// <summary>
/// FAT file system attribute bits
/// </summary>
/// <remarks>
/// This comes from FreeDOS (FAT.H)
/// </remarks>
[Flags]
public enum DosFileAttributes {
    /// <summary>
    /// No attributes set
    /// </summary>
    Normal = 0x0,
    /// <summary>
    /// Cannot be written to
    /// </summary>
    ReadOnly = 0x1,
    /// <summary>
    /// It's not a part of normal file listings
    /// </summary>
    Hidden = 0x2,
    /// <summary>
    /// Is it an operating system file
    /// </summary>
    System = 0x4,
    /// <summary>
    /// Is it a DOS volume letter
    /// </summary>
    VolumeId = 0x8,
    /// <summary>
    /// Is it a subdirectory
    /// </summary>
    Directory = 0x10,
    /// <summary>
    /// Has it been archived
    /// <remarks>This is a legacy CP/M flag, that is always true on DOS/Windows.</remarks>
    /// </summary>
    Archive = 0x20,
}