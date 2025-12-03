namespace Spice86.Shared.Interfaces;
using Spice86.Shared.Emulator.Keyboard;

using System;

/// <summary>
/// Events fired by the emulator UI when a keyboard key is pressed or released.
/// </summary>
public interface IGuiKeyboardEvents {
    /// <summary>
    /// Fired when a keyboard key is released.
    /// </summary>
    public event EventHandler<KeyboardEventArgs>? KeyUp;

    /// <summary>
    /// Fired when a keyboard key is pressed.
    /// </summary>
    public event EventHandler<KeyboardEventArgs>? KeyDown;
}