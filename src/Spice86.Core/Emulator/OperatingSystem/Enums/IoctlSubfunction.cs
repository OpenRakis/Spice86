namespace Spice86.Core.Emulator.OperatingSystem.Enums;

/// <summary>
/// Subfunctions for INT 21h AH=44h (IOCTL - I/O Control for Devices).
/// The subfunction number is passed in AL.
/// </summary>
public enum IoctlSubfunction : byte {
    /// <summary>
    /// AL=00h - Get device information word for a handle.
    /// </summary>
    GetDeviceInformation = 0x00,

    /// <summary>
    /// AL=01h - Set device information word for a handle.
    /// </summary>
    SetDeviceInformation = 0x01,

    /// <summary>
    /// AL=02h - Read from character device control channel.
    /// </summary>
    ReadControlChannel = 0x02,

    /// <summary>
    /// AL=03h - Write to character device control channel.
    /// </summary>
    WriteControlChannel = 0x03,

    /// <summary>
    /// AL=04h - Read from block device control channel.
    /// </summary>
    ReadBlockControlChannel = 0x04,

    /// <summary>
    /// AL=05h - Write to block device control channel.
    /// </summary>
    WriteBlockControlChannel = 0x05,

    /// <summary>
    /// AL=06h - Get input status of a handle.
    /// </summary>
    GetInputStatus = 0x06,

    /// <summary>
    /// AL=07h - Get output status of a handle.
    /// </summary>
    GetOutputStatus = 0x07,

    /// <summary>
    /// AL=08h - Check if block device is removable.
    /// </summary>
    IsDeviceRemovable = 0x08,

    /// <summary>
    /// AL=09h - Check if block device is remote (network).
    /// </summary>
    IsDeviceRemote = 0x09,

    /// <summary>
    /// AL=0Ah - Check if handle is remote (network).
    /// </summary>
    IsHandleRemote = 0x0A,

    /// <summary>
    /// AL=0Bh - Set sharing retry count and delay.
    /// </summary>
    SetSharingRetryCount = 0x0B,

    /// <summary>
    /// AL=0Ch - Generic character device request (codepage switching, etc.).
    /// </summary>
    GenericCharDeviceRequest = 0x0C,

    /// <summary>
    /// AL=0Dh - Generic block device request (get/set device params, serial number, etc.).
    /// </summary>
    GenericBlockDeviceRequest = 0x0D,

    /// <summary>
    /// AL=0Eh - Get logical drive map for a block device.
    /// </summary>
    GetLogicalDriveMap = 0x0E,

    /// <summary>
    /// AL=0Fh - Set logical drive map for a block device.
    /// </summary>
    SetLogicalDriveMap = 0x0F,

    /// <summary>
    /// AL=10h - Query IOCTL handle (DOS 5+).
    /// </summary>
    QueryIoctlHandle = 0x10,

    /// <summary>
    /// AL=11h - Query IOCTL device (DOS 5+).
    /// </summary>
    QueryIoctlDevice = 0x11,
}
