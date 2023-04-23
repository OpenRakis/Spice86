namespace Spice86.Core.Emulator.InterruptHandlers.VGA;

public record struct VgaMode(MemoryModel MemoryModel, ushort Width, ushort Height, byte BitsPerPixel, byte CharacterWidth, byte CharacterHeight, ushort StartSegment);