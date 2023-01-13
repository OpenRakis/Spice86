namespace Spice86.Core.Emulator.Devices.Video;

public enum VideoModes {
    /// <summary>
    /// 320x200, 16 colors from a dynamic palette, mapped at 0xA000 offset in main memory, for a length of 64 KB. Planar.
    /// </summary>
    Ega4bpp320x200 = 0x0D,

    /// <summary>
    /// 640x400, 16 colors from a dynamic palette, mapped at 0xA000 offset in main memory, for a length of 64 KB. Planar.
    /// </summary>
    Ega4bpp640x400 = 0x0E,

    /// <summary>
    /// 320x200, 256 colors from a dynamic palette, mapped at 0xA000 offset in main memory. Planar.
    /// </summary>
    Vga8bpp320x200 = 0x13,
}