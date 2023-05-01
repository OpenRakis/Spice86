namespace Spice86.Shared.Emulator.Keyboard; 

/// <summary>
/// EventArgs for when a keyboard key is down or up.
/// </summary>
/// <param name="Key">The Key pressed or released. Enum taken from Avalonia.</param>
/// <param name="IsPressed">Whether the key is up or down.</param>
public readonly record struct KeyboardEventArgs(Key Key, bool IsPressed);