namespace Spice86.Shared.Interfaces;

using Spice86.Shared.Emulator.Joystick;

using System;

/// <summary>
/// Defines events fired by the UI layer when joystick/gamepad state changes.
/// </summary>
public interface IGuiJoystickEvents {
    /// <summary>
    /// Fired when the state of joystick A changes (axis positions or button presses).
    /// </summary>
    event EventHandler<JoystickStateEventArgs>? JoystickAStateChanged;

    /// <summary>
    /// Fired when the state of joystick B changes (axis positions or button presses).
    /// </summary>
    event EventHandler<JoystickStateEventArgs>? JoystickBStateChanged;
}
