namespace Spice86.Shared.Emulator.Input.Joystick;

/// <summary>
/// Immutable snapshot of one virtual stick's normalized state, after
/// applying calibration, deadzone and a
/// <see cref="Mapping.JoystickProfile"/> on the UI thread.
/// </summary>
/// <remarks>
/// Axis values are normalized to the range <c>[-1.0, 1.0]</c>. A
/// centred stick has <c>X = 0</c>, <c>Y = 0</c>. The Z axis is used
/// by four-axis personalities (<see cref="JoystickType.FourAxis"/>
/// and <see cref="JoystickType.FourAxis2"/>) and the throttle/rudder
/// axis on <see cref="JoystickType.Fcs"/>; <c>R</c> is the second
/// rotational axis, used by some four-axis sticks.
/// </remarks>
/// <param name="X">Normalized X axis position in <c>[-1, 1]</c>.</param>
/// <param name="Y">Normalized Y axis position in <c>[-1, 1]</c>.</param>
/// <param name="Z">Normalized Z (throttle) axis position in <c>[-1, 1]</c>.</param>
/// <param name="R">Normalized R (rudder) axis position in <c>[-1, 1]</c>.</param>
/// <param name="Buttons">Up to four buttons as a 4-bit mask in the
/// low nibble (bit 0 = button 1, bit 3 = button 4). Pressed = 1.</param>
/// <param name="Hat">Eight-way hat direction.</param>
/// <param name="IsConnected">Whether a physical device is currently
/// attached. When false, the gameport behaves as if the stick is
/// unplugged regardless of axis values.</param>
public readonly record struct VirtualStickState(
    float X,
    float Y,
    float Z,
    float R,
    byte Buttons,
    JoystickHatDirection Hat,
    bool IsConnected) {

    /// <summary>
    /// A centred, fully released, disconnected stick. Used as the
    /// initial state of every slot in <c>Gameport</c> until a
    /// connection event arrives.
    /// </summary>
    public static VirtualStickState Disconnected { get; } = new(
        0f, 0f, 0f, 0f, 0, JoystickHatDirection.Centered, false);

    /// <summary>
    /// Returns whether the given zero-based button index is currently
    /// pressed.
    /// </summary>
    /// <param name="buttonIndex">Zero-based button index (0..3).</param>
    /// <returns><see langword="true"/> if the button is pressed.</returns>
    public bool IsButtonPressed(int buttonIndex) {
        if (buttonIndex < 0 || buttonIndex > 3) {
            return false;
        }
        return (Buttons & (1 << buttonIndex)) != 0;
    }
}
