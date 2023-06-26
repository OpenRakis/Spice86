namespace Spice86.Shared.Emulator.Mouse;

/// <summary>
/// Contains the details of the mouse event.
/// </summary>
public readonly record struct MouseButtonEventArgs {
    /// <summary>
    /// Instantiates a new instance of <see cref="MouseButtonEventArgs"/>.
    /// </summary>
    /// <param name="button">Which, if any, button triggered the event</param>
    /// <param name="buttonDown">True if that button is pressed</param>
    public MouseButtonEventArgs(MouseButton button, bool buttonDown) {
        Button = button;
        ButtonDown = buttonDown;
    }

    /// <summary>
    /// Whether the button is pressed.
    /// </summary>
    public bool ButtonDown { get; }

    /// <summary>
    /// Which button triggered the event.
    /// </summary>
    public MouseButton Button { get; }
}