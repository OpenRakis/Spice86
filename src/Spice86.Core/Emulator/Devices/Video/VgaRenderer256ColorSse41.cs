namespace Spice86.Core.Emulator.Devices.Video;

using System;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

/// <summary>
///     SSE4.1-accelerated renderer for 256-color VGA mode.
///     Processes 4 pixels per iteration with manual gather via element extraction.
/// </summary>
internal sealed class VgaRenderer256ColorSse41 : IVgaRenderer256Color {
    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public unsafe void RenderScanline(Span<uint> frameBuffer, ReadOnlySpan<byte> vram,
        uint[] paletteMap, int pixelCount, ref int destOffset) {
        int dest = destOffset;
        int i = 0;

        fixed (byte* vramPtr = vram)
        fixed (uint* destPtr = frameBuffer)
        fixed (uint* palPtr = paletteMap) {
            int count4 = pixelCount & ~3;
            while (i < count4) {
                Vector128<int> idx = Sse41.ConvertToVector128Int32(
                    Sse2.LoadScalarVector128((int*)(vramPtr + i)).AsByte());

                uint p0 = palPtr[(uint)Sse2.ConvertToInt32(idx)];
                uint p1 = palPtr[(uint)Sse41.Extract(idx, 1)];
                uint p2 = palPtr[(uint)Sse41.Extract(idx, 2)];
                uint p3 = palPtr[(uint)Sse41.Extract(idx, 3)];

                Vector128<uint> pixels = Vector128.Create(p0, p1, p2, p3);
                Sse2.Store((uint*)(destPtr + dest), pixels);
                dest += 4;
                i += 4;
            }

            while (i < pixelCount) {
                destPtr[dest++] = palPtr[vramPtr[i++]];
            }
        }

        destOffset = dest;
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public unsafe void RenderDoubledScanline(Span<uint> frameBuffer, ReadOnlySpan<byte> vram,
        uint[] paletteMap, int byteCount, ref int destOffset) {
        int dest = destOffset;
        int i = 0;

        fixed (byte* vramPtr = vram)
        fixed (uint* destPtr = frameBuffer)
        fixed (uint* palPtr = paletteMap) {
            int count4 = byteCount & ~3;
            while (i < count4) {
                Vector128<int> idx = Sse41.ConvertToVector128Int32(
                    Sse2.LoadScalarVector128((int*)(vramPtr + i)).AsByte());

                uint p0 = palPtr[(uint)Sse2.ConvertToInt32(idx)];
                uint p1 = palPtr[(uint)Sse41.Extract(idx, 1)];
                uint p2 = palPtr[(uint)Sse41.Extract(idx, 2)];
                uint p3 = palPtr[(uint)Sse41.Extract(idx, 3)];

                Vector128<uint> doubled0 = Vector128.Create(p0, p0, p1, p1);
                Vector128<uint> doubled1 = Vector128.Create(p2, p2, p3, p3);
                Sse2.Store((uint*)(destPtr + dest), doubled0);
                Sse2.Store((uint*)(destPtr + dest + 4), doubled1);
                dest += 8;
                i += 4;
            }

            while (i < byteCount) {
                uint color = palPtr[vramPtr[i++]];
                destPtr[dest++] = color;
                destPtr[dest++] = color;
            }
        }

        destOffset = dest;
    }
}
