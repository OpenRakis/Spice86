namespace Spice86.Core.Emulator.InterruptHandlers.Input.Mouse;

/// <summary>
///     Press or release counters of the three mouse buttons.
/// </summary>
/// <param name="Left">Counter of the left button.</param>
/// <param name="Right">Counter of the right button.</param>
/// <param name="Middle">Counter of the middle button.</param>
public readonly record struct MouseButtonCountersState(
    MouseButtonCounterState Left,
    MouseButtonCounterState Right,
    MouseButtonCounterState Middle);
