namespace Spice86.Core.Emulator.OperatingSystem.Devices;

/// <summary>
/// Represents a virtual drive (block device) with properties for identification and IOCTL support.
/// This class is the central aggregation point for drive operations, designed to support:
/// - Fixed hard drives (current)
/// - Floppy disk drives (future)
/// - CD-ROM drives with MSCDEX support (future)
/// </summary>
/// <remarks>
/// The VirtualDriveInfo class encapsulates both device identity and behavior, keeping drive
/// management separate from file management. Each drive has associated device handler object
/// that implements IVirtualDriveDevice and handles IOCTL operations.
/// </remarks>
internal sealed class VirtualDriveInfo {
    /// <summary>
    /// Initializes a new instance of the <see cref="VirtualDriveInfo"/> class.
    /// </summary>
    /// <param name="driveLetter">The DOS drive letter (0=A, 1=B, 2=C, etc., or 0xFF for undetermined).</param>
    /// <param name="physicalDriveNumber">The physical drive number (typically 0x00-0x7F for IDE, 0x80+ for SCSI/extended).</param>
    /// <param name="mediaType">The type of media/drive</param>
    /// <param name="isRemovable">Whether the drive supports removable media.</param>
    /// <param name="label">Descriptive label for the drive (e.g., "C: MAIN DISK").</param>
    public VirtualDriveInfo(byte driveLetter, byte physicalDriveNumber, VirtualDriveMediaType mediaType, bool isRemovable, string label = "") {
        DriveLetter = driveLetter;
        PhysicalDriveNumber = physicalDriveNumber;
        MediaType = mediaType;
        IsRemovable = isRemovable;
        Label = label;
    }

    /// <summary>
    /// Gets the DOS drive letter (0=A, 1=B, 2=C, ..., 25=Z, or 0xFF if undetermined).
    /// </summary>
    public byte DriveLetter { get; }

    /// <summary>
    /// Gets the physical drive number used for INT 13h (hard disk int).
    /// Values 0x00-0x7F are floppy/IDE, 0x80+ for SCSI/extended/virtual.
    /// </summary>
    public byte PhysicalDriveNumber { get; }

    /// <summary>
    /// Gets the type of media or drive.
    /// </summary>
    public VirtualDriveMediaType MediaType { get; }

    /// <summary>
    /// Gets whether the drive supports removable media (e.g., CD-ROM, floppy).
    /// </summary>
    public bool IsRemovable { get; }

    /// <summary>
    /// Gets the descriptive label for the drive.
    /// </summary>
    public string Label { get; }

    /// <summary>
    /// Gets the corresponding device letter as a character (A, B, C, ..., Z) or '?' if undetermined.
    /// </summary>
    public char DriveLetterChar => DriveLetter <= 25 ? (char)('A' + DriveLetter) : '?';

    /// <summary>
    /// Returns a human-readable string representation of the virtual drive.
    /// </summary>
    public override string ToString() => $"{DriveLetterChar}: ({MediaType}) - {Label}".TrimEnd(' ', '-');
}
