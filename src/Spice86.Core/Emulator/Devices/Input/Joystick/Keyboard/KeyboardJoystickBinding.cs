namespace Spice86.Core.Emulator.Devices.Input.Joystick.Keyboard;

using Spice86.Shared.Emulator.Input.Joystick;

/// <summary>
/// Single mapping from a host
/// <see cref="Spice86.Shared.Emulator.Keyboard.PhysicalKey"/> to a
/// virtual joystick action.
/// </summary>
/// <remarks>
/// Two bindings on the same <see cref="Axis"/> with opposite
/// <see cref="AxisSign"/> values combine into a tri-state axis:
/// neither key pressed yields <c>0</c>, the negative key yields
/// <c>-1</c>, the positive key yields <c>+1</c>, and both pressed
/// at once cancel each other out (yielding <c>0</c>). Button
/// bindings forward press/release transitions verbatim to the
/// configured stick.
/// </remarks>
public sealed class KeyboardJoystickBinding {
    /// <summary>What kind of action this binding produces.</summary>
    public KeyboardJoystickBindingKind Kind { get; init; }

    /// <summary>Target axis. Only meaningful when
    /// <see cref="Kind"/> is
    /// <see cref="KeyboardJoystickBindingKind.AxisDirection"/>.</summary>
    public JoystickAxis Axis { get; init; }

    /// <summary>Direction sign for the bound axis half: <c>-1</c>
    /// for the negative direction, <c>+1</c> for the positive one.
    /// Any other value is treated as <c>0</c> (the binding is
    /// inert). Only meaningful when <see cref="Kind"/> is
    /// <see cref="KeyboardJoystickBindingKind.AxisDirection"/>.</summary>
    public int AxisSign { get; init; }

    /// <summary>Logical button index. Only meaningful when
    /// <see cref="Kind"/> is
    /// <see cref="KeyboardJoystickBindingKind.Button"/>.</summary>
    public int ButtonIndex { get; init; }

    /// <summary>
    /// Builds a binding that drives one direction of an axis.
    /// </summary>
    /// <param name="axis">Target axis.</param>
    /// <param name="sign"><c>-1</c> for the negative half,
    /// <c>+1</c> for the positive half.</param>
    /// <returns>The constructed binding.</returns>
    public static KeyboardJoystickBinding ForAxis(JoystickAxis axis, int sign) {
        return new KeyboardJoystickBinding {
            Kind = KeyboardJoystickBindingKind.AxisDirection,
            Axis = axis,
            AxisSign = sign,
        };
    }

    /// <summary>
    /// Builds a binding that drives a button.
    /// </summary>
    /// <param name="buttonIndex">Logical button index.</param>
    /// <returns>The constructed binding.</returns>
    public static KeyboardJoystickBinding ForButton(int buttonIndex) {
        return new KeyboardJoystickBinding {
            Kind = KeyboardJoystickBindingKind.Button,
            ButtonIndex = buttonIndex,
        };
    }
}
