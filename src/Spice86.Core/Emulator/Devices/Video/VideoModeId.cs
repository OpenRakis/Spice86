namespace Spice86.Core.Emulator.Devices.Video;

/// <summary>
///     Specifies one of the int 10h video modes.
/// </summary>
public enum VideoModeId {
    /// <summary>
    ///     Monochrome 40x25 text mode.
    /// </summary>
    Text40X25X1 = 0x00,

    /// <summary>
    ///     Color 40x25 text mode (4-bit).
    /// </summary>
    ColorText40X25X4 = 0x01,

    /// <summary>
    ///     Monochrome 80x25 text mode (4-bit).
    /// </summary>
    MonochromeText80X25X4 = 0x02,

    /// <summary>
    ///     Color 80x25 text mode (4-bit).
    /// </summary>
    ColorText80X25X4 = 0x03,

    /// <summary>
    ///     Color 320x200 graphics mode (2-bit).
    /// </summary>
    ColorGraphics320X200X2A = 0x04,

    /// <summary>
    ///     Color 320x200 graphics mode (2-bit).
    /// </summary>
    ColorGraphics320X200X2B = 0x05,

    /// <summary>
    ///     Monochrome 640x200 graphics mode (1-bit).
    /// </summary>
    Graphics640X200X1 = 0x06,

    /// <summary>
    ///     Monochrome 80x25 text mode (1-bit).
    /// </summary>
    Text80X25X1 = 0x07,

    /// <summary>
    ///     Color 320x200 graphics mode (4-bit).
    /// </summary>
    ColorGraphics320X200X4 = 0x0D,

    /// <summary>
    ///     Color 640x200 graphics mode (4-bit).
    /// </summary>
    ColorGraphics640X200X4 = 0x0E,

    /// <summary>
    ///     Monochrome 640x350 graphics mode (1-bit).
    /// </summary>
    Graphics640X350X1 = 0x0F,

    /// <summary>
    ///     Color 640x350 graphics mode (4-bit).
    /// </summary>
    ColorGraphics640X350X4 = 0x10,

    /// <summary>
    ///     Monochrome 640x480 graphics mode (1-bit).
    /// </summary>
    Graphics640X480X1 = 0x11,

    /// <summary>
    ///     Color 640x480 graphics mode (4-bit).
    /// </summary>
    Graphics640X480X4 = 0x12,

    /// <summary>
    ///     Color 320x200 graphics mode (8-bit).
    /// </summary>
    Graphics320X200X8 = 0x13
}