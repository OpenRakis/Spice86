namespace Spice86.Core.Emulator.Devices.Video;

using System;

/// <summary>
///     Strategy interface for 256-color VGA scanline rendering.
///     Implementations provide scalar, SSE4.1, or AVX2 code paths,
///     selected once at construction time based on hardware capabilities.
/// </summary>
public interface IVgaRenderer256Color {
    /// <summary>
    ///     Renders a 256-color scanline with a 1:1 VRAM-byte-to-pixel mapping.
    /// </summary>
    /// <param name="frameBuffer">The destination ARGB frame buffer.</param>
    /// <param name="vram">The VRAM span containing palette indices.</param>
    /// <param name="paletteMap">The 256-entry palette lookup table.</param>
    /// <param name="pixelCount">The number of pixels to render.</param>
    /// <param name="destOffset">Current write position; updated on return.</param>
    void RenderScanline(Span<uint> frameBuffer, ReadOnlySpan<byte> vram,
        uint[] paletteMap, int pixelCount, ref int destOffset);

    /// <summary>
    ///     Renders a 256-color scanline with pixel doubling (each VRAM byte produces two identical pixels).
    /// </summary>
    /// <param name="frameBuffer">The destination ARGB frame buffer.</param>
    /// <param name="vram">The VRAM span containing palette indices.</param>
    /// <param name="paletteMap">The 256-entry palette lookup table.</param>
    /// <param name="byteCount">The number of VRAM bytes to process (produces 2× pixels).</param>
    /// <param name="destOffset">Current write position; updated on return.</param>
    void RenderDoubledScanline(Span<uint> frameBuffer, ReadOnlySpan<byte> vram,
        uint[] paletteMap, int byteCount, ref int destOffset);
}
