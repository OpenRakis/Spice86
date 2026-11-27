namespace Spice86.Core.Emulator.InterruptHandlers.Input.Mouse;

/// <summary>
///     Press or release counter of one mouse button, as INT 33h functions 05h and 06h report it.
/// </summary>
/// <param name="Count">Number of presses or releases since the counter was last read.</param>
/// <param name="LastX">Horizontal position of the last press or release, from 0 to 1.</param>
/// <param name="LastY">Vertical position of the last press or release, from 0 to 1.</param>
public readonly record struct MouseButtonCounterState(int Count, double LastX, double LastY);
