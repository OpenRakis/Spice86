namespace Spice86.Shared.Emulator.Keyboard;

/// <summary>
/// EventArgs for when a keyboard key is down or up.
/// </summary>
/// <param name="Key">The Key pressed or released. Enum taken from Avalonia.</param>
/// <param name="IsPressed">Whether the key is up or down.</param>
/// <param name="ScanCode">The IBM PC scan code, for Keyboard emulation.</param>
/// <param name="AsciiCode">The ASCII code, converted from the scan code. Added to the BIOS keyboard buffer.</param>
public readonly record struct KeyboardEventArgs(Key Key, bool IsPressed, byte? ScanCode, byte? AsciiCode) {
    public static KeyboardEventArgs None { get; } = new(Key.None, false, null, null);
}