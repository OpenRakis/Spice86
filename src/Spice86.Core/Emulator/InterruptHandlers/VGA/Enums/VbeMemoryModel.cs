namespace Spice86.Core.Emulator.InterruptHandlers.VGA.Enums;

/// <summary>
/// VBE memory model types that define how pixel data is organized in video memory.
/// </summary>
public enum VbeMemoryModel : byte {
    /// <summary>
    /// Text mode.
    /// </summary>
    Text = 0x00,

    /// <summary>
    /// CGA graphics mode.
    /// </summary>
    CgaGraphics = 0x01,

    /// <summary>
    /// Hercules graphics mode.
    /// </summary>
    HerculesGraphics = 0x02,

    /// <summary>
    /// Planar mode (EGA/VGA 16-color modes).
    /// </summary>
    Planar = 0x03,

    /// <summary>
    /// Packed pixel mode (256-color and higher).
    /// </summary>
    PackedPixel = 0x04,

    /// <summary>
    /// Non-chain 4, 256-color mode.
    /// </summary>
    NonChain4_256Color = 0x05,

    /// <summary>
    /// Direct color mode (RGB).
    /// </summary>
    DirectColor = 0x06,

    /// <summary>
    /// YUV color mode.
    /// </summary>
    YuvColor = 0x07
}
