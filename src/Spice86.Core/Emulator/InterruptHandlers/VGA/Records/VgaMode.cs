namespace Spice86.Core.Emulator.InterruptHandlers.VGA.Records;

using Spice86.Core.Emulator.InterruptHandlers.VGA.Enums;

/// <summary>
///     Represents a VGA mode.
/// </summary>
/// <param name="MemoryModel">The memory model to use when drawing</param>
/// <param name="Width">The width to tell the BIOS</param>
/// <param name="Height">The height to tell the BIOS</param>
/// <param name="BitsPerPixel">The color depth to use in drawing</param>
/// <param name="CharacterWidth">The default width of characters in this mode</param>
/// <param name="CharacterHeight">The default height of characters in this mode</param>
/// <param name="StartSegment">Which segment this mode uses</param>
public readonly record struct VgaMode(MemoryModel MemoryModel, ushort Width, ushort Height, byte BitsPerPixel, byte CharacterWidth, byte CharacterHeight, ushort StartSegment);