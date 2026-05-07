namespace Spice86.Shared.Emulator.Input.Joystick.Replay;

/// <summary>
/// Logical kind of a single <see cref="JoystickReplayStep"/>.
/// Mirrors the four event types raised by
/// <see cref="Spice86.Shared.Interfaces.IGuiJoystickEvents"/>:
/// axis change, button press/release, hat (POV) change, plus the
/// connect/disconnect transitions that drive
/// <see cref="JoystickConnectionEventArgs"/>.
/// </summary>
public enum JoystickReplayStepType {
    /// <summary>Stick axis position change.</summary>
    Axis,

    /// <summary>Button press or release.</summary>
    Button,

    /// <summary>Hat (POV) direction change.</summary>
    Hat,

    /// <summary>Stick is now plugged in.</summary>
    Connect,

    /// <summary>Stick is now unplugged.</summary>
    Disconnect,
}
