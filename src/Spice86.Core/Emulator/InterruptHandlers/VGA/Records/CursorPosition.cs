namespace Spice86.Core.Emulator.InterruptHandlers.VGA.Records;

/// <summary>
///     Represents a cursor position.
/// </summary>
/// <param name="X">X position in columns</param>
/// <param name="Y">Y position ion rows</param>
/// <param name="Page">Which of the 8 text pages the cursor position is for</param>
public record struct CursorPosition(int X, int Y, int Page);