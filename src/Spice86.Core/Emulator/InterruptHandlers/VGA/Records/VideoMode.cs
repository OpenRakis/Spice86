namespace Spice86.Core.Emulator.InterruptHandlers.VGA.Records;

/// <summary>
/// 
/// </summary>
public record struct VideoMode(VgaMode VgaMode, byte PixelMask, byte[] Palette, byte[] SequencerRegisterValues, byte MiscellaneousRegisterValue, byte[] CrtControllerRegisterValues, byte[] AttributeControllerRegisterValues, byte[] GraphicsControllerRegisterValues);