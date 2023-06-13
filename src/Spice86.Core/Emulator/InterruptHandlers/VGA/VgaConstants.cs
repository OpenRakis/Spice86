namespace Spice86.Core.Emulator.InterruptHandlers.VGA;

using Spice86.Core.Emulator.Memory;

public class VgaConstants {
    /// <summary>
    ///     The segment of the graphics memory.
    /// </summary>
    public const ushort GraphicsSegment = MemoryMap.GraphicVideoMemorySegment;

    /// <summary>
    ///     The segment of the text memory.
    /// </summary>
    public const ushort ColorTextSegment = MemoryMap.ColorTextVideoMemorySegment;

    /// <summary>
    ///     The segment of the monochrome text memory.
    /// </summary>
    public const ushort MonochromeTextSegment = MemoryMap.MonochromeTextVideoMemorySegment;
}