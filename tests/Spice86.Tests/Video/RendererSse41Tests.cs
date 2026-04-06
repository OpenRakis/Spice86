namespace Spice86.Tests.Video;

using Spice86.Core.Emulator.Devices.Video;

using System.Runtime.Intrinsics.X86;

/// <summary>
///     Runs the <see cref="Renderer" /> test suite with the SSE4.1 256-color implementation.
///     Tests are skipped automatically on machines that do not support SSE4.1.
/// </summary>
public sealed class RendererSse41Tests : RendererTestsBase {
    protected override bool IsSupported => Sse41.IsSupported;

    protected override IVgaRenderer256Color Create256ColorRenderer() => new VgaRenderer256ColorSse41();
}
