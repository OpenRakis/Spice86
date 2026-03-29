namespace Spice86.Shared.Emulator.Joystick;

/// <summary>
/// Contains the state of a joystick: axis positions and button states.
/// Axis values range from 0.0 (fully left/up) to 1.0 (fully right/down), with 0.5 being centered.
/// </summary>
public readonly record struct JoystickStateEventArgs {
    /// <summary>
    /// Initializes a new instance of <see cref="JoystickStateEventArgs"/>.
    /// </summary>
    /// <param name="axisX">The X axis position, from 0.0 to 1.0.</param>
    /// <param name="axisY">The Y axis position, from 0.0 to 1.0.</param>
    /// <param name="button1Pressed">Whether button 1 is pressed.</param>
    /// <param name="button2Pressed">Whether button 2 is pressed.</param>
    public JoystickStateEventArgs(double axisX, double axisY, bool button1Pressed, bool button2Pressed) {
        AxisX = Math.Clamp(axisX, 0.0, 1.0);
        AxisY = Math.Clamp(axisY, 0.0, 1.0);
        Button1Pressed = button1Pressed;
        Button2Pressed = button2Pressed;
    }

    /// <summary>
    /// The X axis position, from 0.0 (fully left) to 1.0 (fully right). 0.5 is centered.
    /// </summary>
    public double AxisX { get; }

    /// <summary>
    /// The Y axis position, from 0.0 (fully up) to 1.0 (fully down). 0.5 is centered.
    /// </summary>
    public double AxisY { get; }

    /// <summary>
    /// Whether button 1 is pressed.
    /// </summary>
    public bool Button1Pressed { get; }

    /// <summary>
    /// Whether button 2 is pressed.
    /// </summary>
    public bool Button2Pressed { get; }
}
