namespace Spice86.Core.Emulator.InterruptHandlers.Mscdex;

/// <summary>
/// IOCTL output subfunction codes handled by MSCDEX.
/// </summary>
internal enum MscdexIoctlOutputCode : byte {
    Eject = 0x00,
    LockDoor = 0x01,
    ResetDrive = 0x02,
    ChannelControl = 0x03,
    LoadMedia = 0x05,
}