namespace Spice86.Shared.Emulator.Keyboard;

/// <summary>
/// EventArgs for when a keyboard key is down or up.
/// </summary>
/// <param name="Key">The Key pressed or released. Enum taken from Avalonia.</param>
/// <param name="ScanCode">The IBM PC scan code, for Keyboard emulation.</param>
public readonly record struct KeyboardEventArgs(Key Key, byte? ScanCode) {
    /// <summary>
    /// Static property representing an empty KeyboardEventArgs instance.
    /// </summary>
    public static KeyboardEventArgs None { get; } = new(Key.None, null);
}