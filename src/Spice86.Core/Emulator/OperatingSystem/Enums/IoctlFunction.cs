namespace Spice86.Core.Emulator.OperatingSystem.Enums;

/// <summary>
/// DOS IOCTL function codes for INT 21h, AH=44h.
/// These functions provide device-specific control operations for both character and block devices.
/// </summary>
/// <remarks>
/// References:
/// - MS-DOS 4.0 source code
/// - Adams - Writing DOS Device Drivers in C (1990), Chapter 8
/// - RBIL (Ralf Brown's Interrupt List)
/// </remarks>
public enum IoctlFunction : byte {
    /// <summary>
    /// Get Device Information (AL=00h).
    /// Returns device information word in DX.
    /// For character devices: bit 7 set, bits 0-6 contain device attributes.
    /// For files/block devices: bit 7 clear, bits 0-5 contain drive number, bits 6-15 contain attributes.
    /// </summary>
    GetDeviceInformation = 0x00,

    /// <summary>
    /// Set Device Information (AL=01h).
    /// Sets device information from DL. DH must be 0.
    /// Only certain bits can be modified (typically bits 5-6 for binary/cooked mode).
    /// </summary>
    SetDeviceInformation = 0x01,

    /// <summary>
    /// Read from Device Control Channel (AL=02h).
    /// Character devices only. Reads CX bytes from the device's control channel to DS:DX.
    /// Requires device to support IOCTL (bit 14 set in device attributes).
    /// </summary>
    ReadFromControlChannel = 0x02,

    /// <summary>
    /// Write to Device Control Channel (AL=03h).
    /// Character devices only. Writes CX bytes from DS:DX to the device's control channel.
    /// Requires device to support IOCTL (bit 14 set in device attributes).
    /// </summary>
    WriteToControlChannel = 0x03,

    /// <summary>
    /// Read from Block Device Control Channel (AL=04h).
    /// Block devices only. Reads CX bytes from drive BL's control channel to DS:DX.
    /// DOS 3.2+.
    /// </summary>
    ReadFromBlockDeviceControlChannel = 0x04,

    /// <summary>
    /// Write to Block Device Control Channel (AL=05h).
    /// Block devices only. Writes CX bytes from DS:DX to drive BL's control channel.
    /// DOS 3.2+.
    /// </summary>
    WriteToBlockDeviceControlChannel = 0x05,

    /// <summary>
    /// Get Input Status (AL=06h).
    /// Returns AL=FFh if input is available, 00h if not.
    /// For devices, checks the EOF bit; for files, checks if at EOF.
    /// </summary>
    GetInputStatus = 0x06,

    /// <summary>
    /// Get Output Status (AL=07h).
    /// Returns AL=FFh if device is ready, 00h if busy.
    /// </summary>
    GetOutputStatus = 0x07,

    /// <summary>
    /// Check if Block Device is Removable (AL=08h).
    /// Returns AX=0 if removable, AX=1 if not removable.
    /// DOS 3.0+.
    /// </summary>
    IsBlockDeviceRemovable = 0x08,

    /// <summary>
    /// Check if Block Device is Remote (AL=09h).
    /// Returns DX with bit 12 set if remote, clear if local.
    /// Also returns other device attributes in DX.
    /// DOS 3.1+.
    /// </summary>
    IsBlockDeviceRemote = 0x09,

    /// <summary>
    /// Check if Handle is Remote (AL=0Ah).
    /// Returns DX with bit 15 set if handle refers to a remote file.
    /// DOS 3.1+.
    /// </summary>
    IsHandleRemote = 0x0A,

    /// <summary>
    /// Set Sharing Retry Count (AL=0Bh).
    /// Sets the number of retries (CX) and delay between retries (DX) for sharing violations.
    /// DOS 3.0+.
    /// </summary>
    SetSharingRetryCount = 0x0B,

    /// <summary>
    /// Generic IOCTL for Character Devices (AL=0Ch).
    /// CH = major code (category), CL = minor code (function).
    /// DS:DX points to parameter block.
    /// DOS 3.2+.
    /// </summary>
    GenericIoctlForCharacterDevices = 0x0C,

    /// <summary>
    /// Generic IOCTL for Block Devices (AL=0Dh).
    /// CH = major code (category), CL = minor code (function).
    /// DS:DX points to parameter block.
    /// BL = drive number (0=default, 1=A:, 2=B:, etc.).
    /// DOS 3.2+.
    /// </summary>
    GenericIoctlForBlockDevices = 0x0D,

    /// <summary>
    /// Get Logical Drive Map (AL=0Eh).
    /// Returns in AL the last drive letter used to access the drive.
    /// Returns 0 if only one logical drive letter assigned to the physical drive.
    /// DOS 3.2+.
    /// </summary>
    GetLogicalDriveMap = 0x0E,

    /// <summary>
    /// Set Logical Drive Map (AL=0Fh).
    /// Maps a logical drive letter to the drive in BL.
    /// DOS 3.2+.
    /// </summary>
    SetLogicalDriveMap = 0x0F,

    /// <summary>
    /// Query Generic IOCTL Capability for Handle (AL=10h).
    /// Tests whether a generic IOCTL call is supported by the device.
    /// CH = category code, CL = function code.
    /// DOS 5.0+.
    /// </summary>
    QueryGenericIoctlCapabilityForHandle = 0x10,

    /// <summary>
    /// Query Generic IOCTL Capability for Block Device (AL=11h).
    /// Tests whether a generic IOCTL call is supported by the block device.
    /// BL = drive number, CH = category code, CL = function code.
    /// DOS 5.0+.
    /// </summary>
    QueryGenericIoctlCapabilityForBlockDevice = 0x11
}
