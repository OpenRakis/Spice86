namespace Spice86.Core.Emulator.OperatingSystem.Enums;

/// <summary>
/// DOS Generic IOCTL command codes for block devices (INT 21h, AH=44h, AL=0Dh).
/// These are the minor function codes (CL register) with major code 08h (disk drive) in CH.
/// </summary>
/// <remarks>
/// References:
/// - MS-DOS 4.0 source code (MSDOS.ASM, IOCTL.ASM)
/// - Adams - Writing DOS Device Drivers in C (1990), Chapter 8
/// - RBIL (Ralf Brown's Interrupt List)
/// </remarks>
public enum GenericBlockDeviceCommand : byte {
    /// <summary>
    /// Set Device Parameters (CL=40h).
    /// Sets device parameters using a Device Parameter Block (DPB).
    /// DS:DX points to device parameter block.
    /// DOS 3.2+.
    /// </summary>
    SetDeviceParameters = 0x40,

    /// <summary>
    /// Write Track (CL=41h).
    /// Writes sectors to a track.
    /// Used for low-level disk formatting.
    /// DOS 3.2+.
    /// </summary>
    WriteTrack = 0x41,

    /// <summary>
    /// Format Track (CL=42h).
    /// Formats a track on the disk.
    /// DS:DX points to format descriptor.
    /// DOS 3.2+.
    /// </summary>
    FormatTrack = 0x42,

    /// <summary>
    /// Set Media ID (CL=46h).
    /// Sets the volume serial number for the drive.
    /// DS:DX points to media ID structure.
    /// DOS 4.0+.
    /// </summary>
    SetMediaId = 0x46,

    /// <summary>
    /// Set Volume Serial Number (CL=46h).
    /// Alternative name for SetMediaId - sets the volume serial number.
    /// DS:DX points to volume information structure.
    /// DOS 4.0+.
    /// </summary>
    SetVolumeSerialNumber = 0x46,

    /// <summary>
    /// Get Device Parameters (CL=60h).
    /// Gets device parameters in a Device Parameter Block (DPB).
    /// DS:DX points to buffer to receive device parameter block.
    /// DOS 3.2+.
    /// </summary>
    GetDeviceParameters = 0x60,

    /// <summary>
    /// Read Track (CL=61h).
    /// Reads sectors from a track.
    /// Used for low-level disk access.
    /// DOS 3.2+.
    /// </summary>
    ReadTrack = 0x61,

    /// <summary>
    /// Verify Track (CL=62h).
    /// Verifies sectors on a track.
    /// DOS 3.2+.
    /// </summary>
    VerifyTrack = 0x62,

    /// <summary>
    /// Get Media ID (CL=66h).
    /// Gets the volume serial number, volume label, and file system type.
    /// DS:DX points to buffer to receive media ID/volume information.
    /// DOS 4.0+.
    /// </summary>
    GetMediaId = 0x66,

    /// <summary>
    /// Get Volume Serial Number (CL=66h).
    /// Alternative name for GetMediaId - gets volume serial number and label.
    /// DS:DX points to buffer to receive volume information structure.
    /// DOS 4.0+.
    /// </summary>
    GetVolumeSerialNumber = 0x66,

    /// <summary>
    /// Sense Media Type (CL=68h).
    /// Determines the media type for a logical drive.
    /// DS:DX points to a 2-byte buffer.
    /// DOS 5.0+.
    /// </summary>
    SenseMediaType = 0x68
}

/// <summary>
/// Major category codes for Generic IOCTL (CH register value).
/// </summary>
public enum GenericIoctlCategory : byte {
    /// <summary>
    /// Unknown device type.
    /// </summary>
    Unknown = 0x00,

    /// <summary>
    /// COM (serial) ports.
    /// </summary>
    SerialPort = 0x01,

    /// <summary>
    /// Console (CON) device.
    /// </summary>
    Console = 0x03,

    /// <summary>
    /// IOCTL for code page switching.
    /// DOS 3.3+.
    /// </summary>
    CodePageSwitching = 0x05,

    /// <summary>
    /// Disk drive (block device).
    /// This is the category used for block device operations.
    /// </summary>
    DiskDrive = 0x08
}
