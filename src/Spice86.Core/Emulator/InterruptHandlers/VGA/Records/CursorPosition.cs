namespace Spice86.Core.Emulator.InterruptHandlers.VGA.Records;

/// <summary>
/// Represents a cursor position.
/// </summary>
public record struct CursorPosition(int X, int Y, int Page);