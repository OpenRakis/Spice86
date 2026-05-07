namespace Spice86.Shared.Emulator.Input.Joystick;

/// <summary>
/// Raised by the UI layer when a joystick button is pressed or
/// released. Carries the post-mapping logical button index, not
/// the raw SDL button index.
/// </summary>
/// <param name="StickIndex">Zero-based stick index (0 = stick A,
/// 1 = stick B).</param>
/// <param name="ButtonIndex">Zero-based logical button index on
/// that stick. Valid values are 0 and 1 for the IBM gameport's two
/// buttons per stick.</param>
/// <param name="IsPressed"><see langword="true"/> for press,
/// <see langword="false"/> for release.</param>
public readonly record struct JoystickButtonEventArgs(
    int StickIndex,
    int ButtonIndex,
    bool IsPressed);
