namespace Spice86.Core.Emulator.OperatingSystem.Enums;

/// <summary>
/// Device information word flags returned by IOCTL function 00h (Get Device Information).
/// The interpretation differs for character devices vs. files/block devices.
/// </summary>
/// <remarks>
/// References:
/// - MS-DOS 4.0 source code (DEVSYM.ASM)
/// - Adams - Writing DOS Device Drivers in C (1990), Chapter 4, Table 4-3
/// - RBIL (Ralf Brown's Interrupt List) INT 21/AH=44h/AL=00h
/// </remarks>
[Flags]
public enum DeviceInformationFlags : ushort {
    // Character Device Flags (when bit 7 is set)

    /// <summary>
    /// Bit 0: Console input device (stdin).
    /// For character devices only.
    /// </summary>
    ConsoleInputDevice = 0x0001,

    /// <summary>
    /// Bit 1: Console output device (stdout).
    /// For character devices only.
    /// </summary>
    ConsoleOutputDevice = 0x0002,

    /// <summary>
    /// Bit 2: Null device.
    /// For character devices only.
    /// </summary>
    NullDevice = 0x0004,

    /// <summary>
    /// Bit 3: Clock device.
    /// For character devices only.
    /// </summary>
    ClockDevice = 0x0008,

    /// <summary>
    /// Bit 4: Special device (reserved).
    /// For character devices only.
    /// </summary>
    SpecialDevice = 0x0010,

    /// <summary>
    /// Bit 5: Binary mode (raw mode).
    /// When set, device is in binary/raw mode.
    /// When clear, device is in cooked mode (processes Ctrl+C, Ctrl+S, etc.).
    /// Can be modified with IOCTL function 01h.
    /// For character devices only.
    /// </summary>
    BinaryMode = 0x0020,

    /// <summary>
    /// Bit 6: End-of-file on input.
    /// When clear, EOF has not been reached.
    /// When set, EOF has been reached.
    /// For character devices only.
    /// </summary>
    EndOfFile = 0x0040,

    /// <summary>
    /// Bit 7: Character device flag.
    /// When set, this is a character device.
    /// When clear, this is a file or block device.
    /// This bit distinguishes between the two interpretation modes.
    /// </summary>
    IsCharacterDevice = 0x0080,

    // File/Block Device Flags (when bit 7 is clear)

    /// <summary>
    /// Bits 0-5: Drive number for files/block devices (when bit 7 is clear).
    /// 0 = A:, 1 = B:, 2 = C:, etc.
    /// Mask: 0x003F.
    /// </summary>
    DriveNumberMask = 0x003F,

    /// <summary>
    /// Bit 6: File has not been written to.
    /// For files only (when bit 7 is clear).
    /// </summary>
    FileNotWritten = 0x0040,

    // Bit 7 is clear for files/block devices

    // Common Flags (bits 8-15)

    /// <summary>
    /// Bit 11: Network drive/remote file.
    /// When set, indicates the drive is on a network or the file is remote.
    /// DOS 3.1+.
    /// </summary>
    IsRemote = 0x0800,

    /// <summary>
    /// Bit 12: Reserved (should be 0).
    /// </summary>
    Reserved12 = 0x1000,

    /// <summary>
    /// Bit 13: Reserved (should be 0).
    /// </summary>
    Reserved13 = 0x2000,

    /// <summary>
    /// Bit 14: Device supports IOCTL functions 02h and 03h (control channel).
    /// When set, the device/driver supports IOCTL read/write control channel operations.
    /// </summary>
    SupportsIoctl = 0x4000,

    /// <summary>
    /// Bit 15: Set if this is a character device (same as bit 7 for character devices).
    /// For block devices, this is part of the device attributes.
    /// </summary>
    ExtendedCharacterDeviceFlag = 0x8000,

    // Convenience Combinations

    /// <summary>
    /// Standard input device (stdin): character device + console input.
    /// </summary>
    StandardInput = IsCharacterDevice | ConsoleInputDevice,

    /// <summary>
    /// Standard output device (stdout): character device + console output.
    /// </summary>
    StandardOutput = IsCharacterDevice | ConsoleOutputDevice,

    /// <summary>
    /// Standard error device (stderr): character device + console output.
    /// </summary>
    StandardError = IsCharacterDevice | ConsoleOutputDevice,

    /// <summary>
    /// NUL device: character device + null device.
    /// </summary>
    Null = IsCharacterDevice | NullDevice,

    /// <summary>
    /// Clock device: character device + clock device.
    /// </summary>
    Clock = IsCharacterDevice | ClockDevice
}