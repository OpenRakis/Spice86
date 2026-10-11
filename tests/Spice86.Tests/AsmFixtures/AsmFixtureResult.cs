namespace Spice86.Tests.AsmFixtures;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Devices.Video;

/// <summary>
/// What an oracle may read after a run: memory and CPU state. An <c>AssertResult</c> may only read what the
/// emulated run and the generated run must agree on: memory, video registers and final-state pixels,
/// general and segment registers, <c>SP</c>, flags,
/// and values the program wrote to I/O ports, captured by a handler that the fixture installs in
/// <c>ConfigureMachine</c>. It must not read <c>CS:IP</c> after the end, <c>IsRunning</c>,
/// <c>InterruptShadowing</c>, cycle counts, or any CFG node or block: those are engine checks and stay in
/// <c>MachineTest</c>.
/// </summary>
/// <param name="Memory">Memory of the finished run.</param>
/// <param name="State">CPU state of the finished run.</param>
/// <param name="VideoRenderer">The composed renderer used for deterministic final-state capture.</param>
/// <param name="VideoState">VGA registers and palette of the finished run.</param>
internal sealed record AsmFixtureResult(IMemory Memory, State State, Renderer VideoRenderer, IVideoState VideoState) {
    /// <summary>Renders final VGA state explicitly, independent of scheduler frame timing.</summary>
    public uint[] CaptureVideoFrame() {
        VideoState.IsRenderingDirty = true;
        VideoRenderer.BeginFrame();
        int scanlines = (VideoState.CrtControllerRegisters.VerticalTotalValue + 2) * 2;
        for (int scanline = 0; scanline < scanlines; scanline++) {
            VideoRenderer.RenderScanline();
        }
        VideoRenderer.CompleteFrame();
        uint[] pixels = new uint[VideoRenderer.Width * VideoRenderer.Height];
        VideoRenderer.CopyLastFrame(pixels);
        return pixels;
    }
}
