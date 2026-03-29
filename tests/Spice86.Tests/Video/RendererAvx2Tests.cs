namespace Spice86.Tests.Video;

using Spice86.Core.Emulator.Devices.Video;

using System.Runtime.Intrinsics.X86;

/// <summary>
///     Runs the <see cref="Renderer" /> test suite with the AVX2 256-color implementation.
///     Tests are skipped automatically on machines that do not support AVX2.
/// </summary>
public sealed class RendererAvx2Tests : RendererTestsBase {
    protected override bool IsSupported => Avx2.IsSupported;

    protected override IVgaRenderer256Color Create256ColorRenderer() => new VgaRenderer256ColorAvx2();
}
