namespace Spice86.Core.Emulator.Devices.Input.Joystick;

/// <summary>
/// Immutable snapshot of a single joystick's state.
/// </summary>
/// <param name="Connected">Whether this joystick is connected.</param>
/// <param name="AxisX">X axis position from 0.0 (left) to 1.0 (right).</param>
/// <param name="AxisY">Y axis position from 0.0 (up) to 1.0 (down).</param>
/// <param name="Button1Pressed">Whether button 1 is pressed.</param>
/// <param name="Button2Pressed">Whether button 2 is pressed.</param>
public readonly record struct JoystickSnapshot(
    bool Connected, double AxisX, double AxisY, bool Button1Pressed, bool Button2Pressed);
