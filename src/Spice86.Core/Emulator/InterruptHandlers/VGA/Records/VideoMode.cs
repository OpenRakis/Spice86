namespace Spice86.Core.Emulator.InterruptHandlers.VGA.Records;

using System.Collections.Frozen;

/// <summary>
///     Represents a VGA video mode.
/// </summary>
/// <param name="VgaMode">The mode meta information</param>
/// <param name="PixelMask">The default pixel mask</param>
/// <param name="Palette">The default palette</param>
/// <param name="SequencerRegisterValues">The default sequencer register values</param>
/// <param name="MiscellaneousRegisterValue">The default miscellaneous register value</param>
/// <param name="CrtControllerRegisterValues">The default crt controller register values</param>
/// <param name="AttributeControllerRegisterValues">The default attribute controller register values</param>
/// <param name="GraphicsControllerRegisterValues">The default graphics controller register values</param>
public record struct VideoMode(
    VgaMode VgaMode,
    byte PixelMask,
    byte[] Palette,
    byte[] SequencerRegisterValues,
    byte MiscellaneousRegisterValue,
    byte[] CrtControllerRegisterValues,
    byte[] AttributeControllerRegisterValues,
    byte[] GraphicsControllerRegisterValues
);