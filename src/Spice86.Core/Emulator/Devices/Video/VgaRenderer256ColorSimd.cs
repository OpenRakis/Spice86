namespace Spice86.Core.Emulator.Devices.Video;

using System.Runtime.Intrinsics.X86;

/// <summary>
///     Factory that selects the best available <see cref="IVgaRenderer256Color"/>
///     implementation based on the CPU's SIMD capabilities.
/// </summary>
public static class VgaRenderer256ColorSimd {
    /// <summary>
    ///     Creates the fastest 256-color renderer supported by the current CPU.
    /// </summary>
    /// <returns>An AVX2, SSE4.1, or scalar renderer instance.</returns>
    public static IVgaRenderer256Color CreateBestRenderer() {
        if (Avx2.IsSupported) {
            return new VgaRenderer256ColorAvx2();
        }
        if (Sse41.IsSupported) {
            return new VgaRenderer256ColorSse41();
        }
        return new VgaRenderer256ColorScalar();
    }
}
