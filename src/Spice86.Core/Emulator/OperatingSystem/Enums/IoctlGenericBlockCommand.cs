namespace Spice86.Core.Emulator.OperatingSystem.Enums;

/// <summary>
/// Minor codes (CL register) for IOCTL Generic Block Device Request (INT 21h AX=440Dh).
/// CH must be 08h (disk device category).
/// </summary>
public enum IoctlGenericBlockCommand : byte {
    /// <summary>
    /// CL=46h - Set Volume Serial Number.
    /// </summary>
    SetVolumeSerialNumber = 0x46,

    /// <summary>
    /// CL=60h - Get Device Parameters (BPB, device type, etc.).
    /// </summary>
    GetDeviceParameters = 0x60,

    /// <summary>
    /// CL=66h - Get Volume Serial Number, Volume Label, and File System Type.
    /// </summary>
    GetVolumeInformation = 0x66,
}
