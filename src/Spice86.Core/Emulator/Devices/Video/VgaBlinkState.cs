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

    /// <summary>
    ///     Whether the blink state has changed since the last call to <see cref="ResetChanged"/>.
    /// </summary>
    public bool HasChanged { get; private set; }

    /// <summary>
    ///     Marks the blink state as changed.
    /// </summary>
    public void MarkChanged() => HasChanged = true;

    /// <summary>
    ///     Resets the <see cref="HasChanged"/> flag to <c>false</c>.
    /// </summary>
    public void ResetChanged() => HasChanged = false;
}
