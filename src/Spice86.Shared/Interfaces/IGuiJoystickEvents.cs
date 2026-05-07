namespace Spice86.Shared.Interfaces;

using Spice86.Shared.Emulator.Input.Joystick;

using System;

/// <summary>
/// Events raised by the UI layer to feed joystick input into the
/// emulator. Mirrors <see cref="IGuiKeyboardEvents"/> and
/// <see cref="IGuiMouseEvents"/>: the UI thread is responsible for
/// turning raw SDL input into logical, profile-mapped events,
/// which are then enqueued by <c>InputEventHub</c> and replayed on
/// the emulator thread to drive the Core <c>Gameport</c> device.
/// </summary>
/// <remarks>
/// Implementors include <c>MainWindowViewModel</c> for the
/// Avalonia UI, <c>HeadlessGui</c> for headless mode, and any
/// scripted/MCP harness used in tests. The Core never subscribes
/// to these events directly; it always goes through
/// <c>InputEventHub</c> so that events run on the emulator thread.
/// </remarks>
public interface IGuiJoystickEvents {
    /// <summary>
    /// Fired when a logical joystick axis position changes.
    /// </summary>
    event EventHandler<JoystickAxisEventArgs>? JoystickAxisChanged;

    /// <summary>
    /// Fired when a logical joystick button is pressed or released.
    /// </summary>
    event EventHandler<JoystickButtonEventArgs>? JoystickButtonChanged;

    /// <summary>
    /// Fired when a logical joystick hat (POV) direction changes.
    /// </summary>
    event EventHandler<JoystickHatEventArgs>? JoystickHatChanged;

    /// <summary>
    /// Fired when a virtual stick is connected or disconnected.
    /// </summary>
    event EventHandler<JoystickConnectionEventArgs>? JoystickConnectionChanged;
}
