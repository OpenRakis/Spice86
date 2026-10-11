namespace Spice86.Core.Emulator.Devices.Video;

using System;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

/// <summary>
///     AVX2-accelerated renderer for 256-color VGA mode.
///     Processes 8 pixels per iteration using gather instructions.
/// </summary>
internal sealed class VgaRenderer256ColorAvx2 : IVgaRenderer256Color {
    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public unsafe void RenderScanline(Span<uint> frameBuffer, ReadOnlySpan<byte> vram,
        uint[] paletteMap, int byteCount, ref int destOffset) {
        int dest = destOffset;
        int i = 0;

        fixed (byte* vramPtr = vram)
        fixed (uint* destPtr = frameBuffer)
        fixed (uint* palPtr = paletteMap) {
            int count8 = byteCount & ~7;
            while (i < count8) {
                Vector128<byte> raw = Sse2.LoadScalarVector128((ulong*)(vramPtr + i)).AsByte();
                Vector256<int> idx = Avx2.ConvertToVector256Int32(raw);
                Vector256<int> pixels = Avx2.GatherVector256((int*)palPtr, idx, 4);

                Avx2.Store((int*)(destPtr + dest), pixels);
                dest += 8;
                i += 8;
            }

            while (i < byteCount) {
                uint color = palPtr[vramPtr[i++]];
                destPtr[dest++] = color;
            }
        }

        destOffset = dest;
    }
}
