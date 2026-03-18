namespace Spice86.Core.Emulator.Devices.Video;

/// <summary>
///     Shared mutable state for text-mode attribute blinking, driven by
///     <see cref="VgaTimingEngine"/> on the emulation thread and read by
///     <see cref="Renderer"/> during pixel conversion.
/// </summary>
public sealed class VgaBlinkState {
    /// <summary>
    ///     Whether the blink phase is currently high (visible).
    ///     Written only on the emulation thread; read during rendering.
    /// </summary>
    public bool IsBlinkPhaseHigh { get; set; }
}
