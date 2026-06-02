namespace Spice86.Core.Emulator.InterruptHandlers.Mscdex;

/// <summary>
/// IOCTL input subfunction codes handled by MSCDEX.
/// </summary>
internal enum MscdexIoctlInputCode : byte {
    DeviceHeaderAddress = 0x00,
    CurrentPosition = 0x01,
    ChannelControl = 0x04,
    DeviceStatus = 0x06,
    SectorSize = 0x07,
    VolumeSize = 0x08,
    MediaChanged = 0x09,
    AudioDiskInfo = 0x0A,
    AudioTrackInfo = 0x0B,
    AudioSubchannel = 0x0C,
    UpcCode = 0x0E,
    AudioStatus = 0x0F,
}