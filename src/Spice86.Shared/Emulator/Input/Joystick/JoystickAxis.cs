namespace Spice86.Shared.Emulator.Input.Joystick;

/// <summary>
/// Logical axis carried by a <see cref="JoystickAxisEventArgs"/>.
/// Mirrors the four axes a single virtual gameport stick can carry
/// (X, Y, plus optional Z/throttle and R/rudder).
/// </summary>
public enum JoystickAxis {
    /// <summary>The X axis.</summary>
    X,

    /// <summary>The Y axis.</summary>
    Y,

    /// <summary>The Z (throttle) axis.</summary>
    Z,

    /// <summary>The R (rudder) axis.</summary>
    R
}
