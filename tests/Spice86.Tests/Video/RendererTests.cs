namespace Spice86.Tests.Video;

using Spice86.Core.Emulator.Devices.Video;

/// <summary>
///     Runs the <see cref="Renderer" /> test suite with the scalar (non-SIMD) 256-color implementation.
/// </summary>
public sealed class RendererTests : RendererTestsBase {
    protected override bool IsSupported => true;

    protected override IVgaRenderer256Color Create256ColorRenderer() => new VgaRenderer256ColorScalar();
}
