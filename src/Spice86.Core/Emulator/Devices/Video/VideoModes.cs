namespace Spice86.Core.Emulator.Devices.Video;

public enum VideoModes {
    /// <summary>
    /// 640x400, 16 colors fixed palette, 0xA000 offset in main memory. Planar.
    /// </summary>
    Ega2bpp640x400 = 0x0D,

    /// <summary>
    /// 320x200, 256 colors dynamic palette, 0xA000 offset in main memory. Planar.
    /// </summary>
    Vga8bpp320x200 = 0x13,
}