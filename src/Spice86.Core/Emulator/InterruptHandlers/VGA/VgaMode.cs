namespace Spice86.Core.Emulator.InterruptHandlers.VGA;

using Spice86.Core.Emulator.InterruptHandlers.VGA.Enums;

/// <summary>
/// Represents a VGA mode.
/// </summary>
public record struct VgaMode(MemoryModel MemoryModel, ushort Width, ushort Height, byte BitsPerPixel, byte CharacterWidth, byte CharacterHeight, ushort StartSegment);