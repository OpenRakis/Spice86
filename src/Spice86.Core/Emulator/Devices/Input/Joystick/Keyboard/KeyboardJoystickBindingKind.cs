namespace Spice86.Core.Emulator.Devices.Input.Joystick.Keyboard;

/// <summary>
/// Logical kind of a single
/// <see cref="KeyboardJoystickBinding"/>: either a virtual axis
/// half (one direction of an axis, combined with the opposite-half
/// binding to produce -1 / 0 / +1) or a virtual button.
/// </summary>
public enum KeyboardJoystickBindingKind {
    /// <summary>The bound key drives one direction of a logical
    /// joystick axis.</summary>
    AxisDirection,

    /// <summary>The bound key acts as a logical joystick button.</summary>
    Button,
}
