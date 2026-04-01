namespace Spice86.Core.CLI;

/// <summary>
/// Selects the VGA rendering mode.
/// </summary>
public enum RenderingMode {
    /// <summary>
    /// VGA timing events fire on the emulation thread, driven by the emulated clock.
    /// Deterministic but has higher per-instruction overhead.
    /// </summary>
    Sync,

    /// <summary>
    /// VGA timing events fire on the UI thread, driven by the UI timer.
    /// Non-deterministic but better emulation throughput.
    /// </summary>
    Async
}
