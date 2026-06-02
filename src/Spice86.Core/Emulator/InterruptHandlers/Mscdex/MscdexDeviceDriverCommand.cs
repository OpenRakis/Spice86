namespace Spice86.Core.Emulator.InterruptHandlers.Mscdex;

/// <summary>
/// Device driver request command codes handled by MSCDEX.
/// </summary>
internal enum MscdexDeviceDriverCommand : byte {
    IoctlInput = 0x03,
    IoctlOutput = 0x0C,
    DeviceOpen = 0x0D,
    DeviceClose = 0x0E,
    ReadLong = 0x80,
    ReadLongPrefetch = 0x82,
    Seek = 0x83,
    PlayAudio = 0x84,
    StopAudio = 0x85,
    ResumeAudio = 0x88,
}