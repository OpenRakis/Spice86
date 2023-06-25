namespace Spice86.Converters;

using Spice86.Shared.Emulator.Mouse;

public static class MouseButtonConverter {
    public static MouseButton ToSpice86MouseButton(this Avalonia.Input.MouseButton button) {
        return button switch {
            Avalonia.Input.MouseButton.Left => MouseButton.Left,
            Avalonia.Input.MouseButton.Right => MouseButton.Right,
            Avalonia.Input.MouseButton.Middle => MouseButton.Middle,
            Avalonia.Input.MouseButton.XButton1 => MouseButton.XButton1,
            Avalonia.Input.MouseButton.XButton2 => MouseButton.XButton2,
            _ => MouseButton.None
        };
    }

    public static Avalonia.Input.MouseButton ToAvaloniaMouseButton(this MouseButton button) {
        return button switch {
            MouseButton.Left => Avalonia.Input.MouseButton.Left,
            MouseButton.Right => Avalonia.Input.MouseButton.Right,
            MouseButton.Middle => Avalonia.Input.MouseButton.Middle,
            MouseButton.XButton1 => Avalonia.Input.MouseButton.XButton1,
            MouseButton.XButton2 => Avalonia.Input.MouseButton.XButton2,
            _ => Avalonia.Input.MouseButton.None
        };
    }
}