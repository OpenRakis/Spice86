namespace Spice86.Core.Emulator.OperatingSystem.Devices;

/// <summary>
/// Interface for virtual drive device operations (block devices: hard drives, CD-ROM, floppy disks).
/// This contract supports IOCTL operations and future extensibility for CD-ROM (MSCDEX) and floppy media.
/// </summary>
/// <remarks>
/// Supports generic IOCTL operations needed for MSCDEX (CD-ROM), floppy disks, and block device management.
/// </remarks>
internal interface IVirtualDriveDevice : IVirtualDevice {
    /// <summary>
    /// Gets whether the drive medium is removable (e.g., CD-ROM, floppy disk).
    /// </summary>
    bool IsRemovable { get; }

    /// <summary>
    /// Gets the physical drive number (0-based), as mapped to DOS drive letters (A, B, C, etc.).
    /// </summary>
    byte PhysicalDriveNumber { get; }

    /// <summary>
    /// Gets the media type of the drive (fixed, floppy, CD-ROM, etc.).
    /// </summary>
    VirtualDriveMediaType MediaType { get; }

    /// <summary>
    /// Checks if the medium is currently present in the drive.
    /// Used for removable media (floppy, CD-ROM) to determine if media is loaded.
    /// </summary>
    /// <returns>True if media is present, false otherwise.</returns>
    bool IsMediaPresent();

    /// <summary>
    /// Attempts to read a sector from the drive at the given logical block address (LBA).
    /// Future MSCDEX/CD-ROM support will use this for audio and data sector reads.
    /// </summary>
    /// <param name="lba">Logical Block Address to read from.</param>
    /// <param name="buffer">Physical address of buffer to read into.</param>
    /// <param name="bufferSize">Maximum bytes to read.</param>
    /// <param name="bytesRead">Number of bytes successfully read.</param>
    /// <returns>True if read succeeded, false on error.</returns>
    bool TryReadSector(uint lba, uint buffer, uint bufferSize, out uint bytesRead);

    /// <summary>
    /// Gets device-specific information for IOCTL operations.
    /// Used for block device parameter queries (cylinders, heads, sectors per track, etc.).
    /// </summary>
    /// <returns>Device information structure pointer or 0 if not supported.</returns>
    uint GetDeviceParameters();

    /// <summary>
    /// Handles generic IOCTL commands for block devices (Get Media Parameters, Track Layout, etc.).
    /// This is the extensibility point for future IOCTL features.
    /// </summary>
    /// <param name="command">IOCTL command code (0x00-0x0F for standard DOS block device commands).</param>
    /// <param name="parameterBuffer">Physical address of command parameter block.</param>
    /// <returns>True if command was handled successfully.</returns>
    bool TryHandleIoctl(byte command, uint parameterBuffer);
}
