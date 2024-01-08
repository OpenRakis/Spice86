namespace Spice86.Core.Emulator.OperatingSystem.Enums;

/// <summary>
/// Represents a set of flags that describe the attributes of a virtual DOS device.
/// </summary>
[Flags]
public enum DeviceAttributes {
    /// <summary>
    /// The device is the current standard input device.
    /// </summary>
    CurrentStdin = 0x1,

    /// <summary>
    /// The device is the current standard output device.
    /// </summary>
    CurrentStdout = 0x2,

    /// <summary>
    /// The device is the null device.
    /// </summary>
    CurrentNull = 0x4,

    /// <summary>
    /// The device is the system clock.
    /// </summary>
    CurrentClock = 0x8,

    /// <summary>
    /// The device has special characteristics.
    /// </summary>
    Special = 0x10,

    /// <summary>
    /// The device is a FAT device.
    /// </summary>
    FatDevice = 0x2000,

    /// <summary>
    /// The device supports IOCTL (input/output control) operations.
    /// </summary>
    Ioctl = 0x4000,

    /// <summary>
    /// The device is a character device.
    /// </summary>
    Character = 0x8000
}