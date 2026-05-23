namespace Spice86.Shared.Emulator.Input.Joystick;

/// <summary>
/// Raised by the UI layer when a joystick hat (POV) direction
/// changes.
/// </summary>
/// <param name="StickIndex">Zero-based stick index (0 = stick A,
/// 1 = stick B).</param>
/// <param name="Direction">Eight-way hat direction, including
/// <see cref="JoystickHatDirection.Centered"/> when released.</param>
public readonly record struct JoystickHatEventArgs(
    int StickIndex,
    JoystickHatDirection Direction);
