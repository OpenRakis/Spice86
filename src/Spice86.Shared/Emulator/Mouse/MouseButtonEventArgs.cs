namespace Spice86.Shared.Emulator.Mouse;

public class MouseButtonEventArgs : EventArgs {
    public MouseButtonEventArgs(MouseButton button, bool buttonDown) {
        Button = button switch {
            MouseButton.Left => MouseButton.Left,
            MouseButton.Right => MouseButton.Right,
            MouseButton.Middle => MouseButton.Middle,
            _ => throw new ArgumentOutOfRangeException(nameof(button), button, "Unhandled mouse button")
        };
        ButtonDown = buttonDown;
    }

    public bool ButtonDown { get; }

    public MouseButton Button { get; }
}