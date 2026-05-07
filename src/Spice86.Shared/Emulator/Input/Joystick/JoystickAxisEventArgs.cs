namespace Spice86.Shared.Emulator.Input.Joystick;

/// <summary>
/// Raised by the UI layer when a joystick axis position changes.
/// Carries the post-mapping logical position (after profile,
/// deadzone, calibration), so the emulator core never sees raw SDL
/// data.
/// </summary>
/// <param name="StickIndex">Zero-based stick index (0 = stick A,
/// 1 = stick B).</param>
/// <param name="Axis">Which logical axis on that stick the event
/// targets.</param>
/// <param name="Value">Normalized axis value in <c>[-1.0, 1.0]</c>.
/// 0 means centred.</param>
public readonly record struct JoystickAxisEventArgs(
    int StickIndex,
    JoystickAxis Axis,
    float Value);
