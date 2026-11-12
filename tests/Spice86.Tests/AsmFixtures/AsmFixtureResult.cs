namespace Spice86.Tests.AsmFixtures;

using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Devices.Video;

/// <summary>
/// What an oracle may read after a run: memory, video state and this run's observations. An
/// <c>AssertResult</c> may only read what the emulated run and the generated run must agree on: memory,
/// video registers and final-state pixels, values the
/// program wrote to I/O ports captured by a handler that the fixture installs in
/// <c>ConfigureMachine</c>, and the run's own observations. It must not read <c>CS:IP</c> after the end,
/// <c>IsRunning</c>, <c>InterruptShadowing</c>, cycle counts, or any CFG node or block: those are
/// engine checks and live in the fixture's engine assertions instead. Registers and flags a check
/// needs are stored to result memory by the program itself before it ends.
/// </summary>
/// <param name="Memory">Memory of the finished run.</param>
/// <param name="VideoRenderer">The composed renderer used for deterministic final-state capture.</param>
/// <param name="VideoState">VGA registers and palette of the finished run.</param>
/// <param name="Observations">Behavioral observations captured for this run alone.</param>
internal sealed record AsmFixtureResult(
    IMemory Memory,
    Renderer VideoRenderer,
    IVideoState VideoState,
    AsmRunObservations Observations) {
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
