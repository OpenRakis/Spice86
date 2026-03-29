namespace Spice86.Core.Emulator.Devices.Video;

using System;

/// <summary>
///     Scalar (non-SIMD) batch renderer for 256-color VGA mode.
///     Processes 4 pixels per iteration to improve instruction-level parallelism.
/// </summary>
internal sealed class VgaRenderer256ColorScalar : IVgaRenderer256Color {
    /// <inheritdoc />
    public void RenderDoubledScanline(Span<uint> frameBuffer, ReadOnlySpan<byte> vram,
        uint[] paletteMap, int byteCount, ref int destOffset) {
        int dest = destOffset;
        int i = 0;
        int count4 = byteCount & ~3;
        while (i < count4) {
            uint c0 = paletteMap[vram[i]];
            uint c1 = paletteMap[vram[i + 1]];
            uint c2 = paletteMap[vram[i + 2]];
            uint c3 = paletteMap[vram[i + 3]];
            frameBuffer[dest] = c0;
            frameBuffer[dest + 1] = c0;
            frameBuffer[dest + 2] = c1;
            frameBuffer[dest + 3] = c1;
            frameBuffer[dest + 4] = c2;
            frameBuffer[dest + 5] = c2;
            frameBuffer[dest + 6] = c3;
            frameBuffer[dest + 7] = c3;
            dest += 8;
            i += 4;
        }
        while (i < byteCount) {
            uint color = paletteMap[vram[i++]];
            frameBuffer[dest++] = color;
            frameBuffer[dest++] = color;
        }
        destOffset = dest;
    }
}
