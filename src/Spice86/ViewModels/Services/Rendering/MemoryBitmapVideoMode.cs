namespace Spice86.ViewModels.Services.Rendering;

/// <summary>
///     Enumerates the legacy video modes available for rendering an arbitrary memory region as a bitmap.
///     These modes are used purely for debugging and reverse engineering, independently of the
///     emulator's current VGA state.
/// </summary>
public enum MemoryBitmapVideoMode {
    /// <summary>
    ///     Raw 8-bit indexed color. Each byte is a palette index, laid out linearly.
    /// </summary>
    Raw8Bpp,

    /// <summary>
    ///     VGA Mode 13h (320x200, 256 colors). Linear 8bpp, each byte is a direct palette index.
    /// </summary>
    Vga256Color,

    /// <summary>
    ///     VGA Mode X / Unchained 256-color. 4 planes interleaved by pixel (plane = pixel index mod 4).
    ///     Used in 320x240 and other tweaked VGA modes. Each plane holds every 4th pixel.
    /// </summary>
    VgaModeX,

    /// <summary>
    ///     EGA/VGA 16-color planar mode (modes 0Dh, 0Eh, 10h, 12h).
    ///     4 bit planes, one bit per pixel per plane, 8 pixels per byte. MSB = leftmost pixel.
    /// </summary>
    Ega16Color,

    /// <summary>
    ///     4bpp packed (non-planar). 2 pixels per byte, high nibble = left pixel, low nibble = right pixel.
    ///     Useful for reverse engineering non-standard 16-color data that is not stored in planar format.
    /// </summary>
    Packed4Bpp,

    /// <summary>
    ///     CGA 4-color mode (modes 04h, 05h). 2 bits per pixel, even/odd scanline interleaving.
    ///     Even scanlines in the first half of memory, odd scanlines in the second half.
    /// </summary>
    Cga4Color,

    /// <summary>
    ///     CGA high-resolution 2-color mode (mode 06h). 1 bit per pixel, even/odd scanline interleaving.
    /// </summary>
    Cga2Color,

    /// <summary>
    ///     Linear 1bpp monochrome. 1 bit per pixel, no scanline interleaving. MSB = leftmost pixel.
    ///     Useful for examining raw bitmap data, font tables, or sprite masks.
    /// </summary>
    Linear1Bpp,

    /// <summary>
    ///     Text mode. Each character cell is a (character code, attribute) byte pair.
    ///     Rendered with an 8x16 built-in VGA ROM font.
    ///     Width/height are in character cells. Output is 8x wider and 16x taller.
    /// </summary>
    Text
}
