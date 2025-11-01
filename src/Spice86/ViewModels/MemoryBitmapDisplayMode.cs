namespace Spice86.ViewModels;

/// <summary>
/// Defines the display modes for rendering memory as bitmaps.
/// </summary>
public enum MemoryBitmapDisplayMode {
    /// <summary>
    /// VGA 256-color indexed mode (8 bits per pixel).
    /// </summary>
    Vga8Bpp,

    /// <summary>
    /// CGA 4-color graphics mode.
    /// </summary>
    Cga4Color,

    /// <summary>
    /// EGA 16-color graphics mode.
    /// </summary>
    Ega16Color,

    /// <summary>
    /// Text mode with IBM PC fonts.
    /// </summary>
    TextMode,

    /// <summary>
    /// Hercules monochrome graphics mode.
    /// </summary>
    HerculesMonochrome
}
