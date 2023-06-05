namespace Spice86.Core.Emulator.InterruptHandlers.VGA;

public class VgaConstants {
    /// <summary>
    ///     The segment of the graphics memory.
    /// </summary>
    public const ushort GraphicsSegment = 0xA000;

    /// <summary>
    ///     The segment of the text memory.
    /// </summary>
    public const ushort ColorTextSegment = 0xB800;

    /// <summary>
    ///     The segment of the monochrome text memory.
    /// </summary>
    public const ushort MonochromeTextSegment = 0xB000;
}