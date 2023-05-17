namespace Spice86.Core.Emulator.InterruptHandlers.VGA.Records;

using Spice86.Core.Emulator.InterruptHandlers.VGA.Enums;

/// <summary>
/// Represents a graphics operation.
/// </summary>
internal record struct GraphicsOperation(VgaMode VgaMode, int LineLength, int DisplayStart, MemoryAction MemoryAction, int X, int Y, byte[] Pixels, int Width, int Height, int Lines);