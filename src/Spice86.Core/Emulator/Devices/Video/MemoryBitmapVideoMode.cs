namespace Spice86.Core.Emulator.Devices.Video;

/// <summary>
///     Enumerates the legacy video modes available for rendering an arbitrary memory region as a bitmap.
/// </summary>
public enum MemoryBitmapVideoMode {
    /// <summary>
    ///     Raw 8-bit indexed color. Each byte in the memory region is a palette index.
    /// </summary>
    Raw8Bpp,

    /// <summary>
    ///     VGA Mode 13h (320×200, 256 colors). Each byte is a direct palette index (linear).
    /// </summary>
    Vga256Color,

    /// <summary>
    ///     EGA 16-color planar mode. 4 planes, one bit per pixel per plane, 8 pixels per byte.
    /// </summary>
    Ega16Color,

    /// <summary>
    ///     CGA 4-color mode. 2 bits per pixel, interleaved between even/odd scanline banks.
    /// </summary>
    Cga4Color,

    /// <summary>
    ///     CGA 2-color (monochrome) mode. 1 bit per pixel, interleaved between even/odd scanline banks.
    /// </summary>
    Cga2Color,

    /// <summary>
    ///     Text mode. Character code + attribute pairs rendered with an 8×16 ROM font.
    /// </summary>
    Text
}
