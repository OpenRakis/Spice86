namespace Spice86.Core.Emulator.Devices.Video;

using System.Threading;

/// <summary>
///     Shared mutable state for text-mode attribute blinking, driven by
///     <see cref="VgaTimingEngine"/> on the emulation thread and read by
///     <see cref="Renderer"/> during pixel conversion.
/// </summary>
public sealed class VgaBlinkState {
    // Backing ints so we can use Volatile/Interlocked semantics across threads.
    private int _isBlinkPhaseHigh;
    private int _hasChanged;

    /// <summary>
    ///     Whether the blink phase is currently high (visible).
    ///     Written only on the emulation thread; read during rendering.
    /// </summary>
    public bool IsBlinkPhaseHigh {
        get => Volatile.Read(ref _isBlinkPhaseHigh) != 0;
        set => Volatile.Write(ref _isBlinkPhaseHigh, value ? 1 : 0);
    }

    /// <summary>
    ///     Whether the blink state has changed since the last call to <see cref="ResetChanged"/>.
    /// </summary>
    public bool HasChanged => Volatile.Read(ref _hasChanged) != 0;

    /// <summary>
    ///     Marks the blink state as changed.
    /// </summary>
    public void MarkChanged() => Volatile.Write(ref _hasChanged, 1);

    /// <summary>
    ///     Resets the <see cref="HasChanged"/> flag to <c>false</c>.
    /// </summary>
    public void ResetChanged() => Interlocked.Exchange(ref _hasChanged, 0);
}
