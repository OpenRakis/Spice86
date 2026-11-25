namespace Spice86.Core.Emulator.Devices.Video;

using Spice86.Core.Emulator.Devices.Video.Registers;

/// <summary>
///     Complete state of the VGA registers, as captured by <see cref="VgaIoPortHandler.CaptureState" />.
/// </summary>
/// <remarks>
///     Each register file is stored as the values its data port reads, one byte per index from index 0. The
///     beam position is not stored: it follows the emulated clock.
/// </remarks>
/// <param name="SequencerAddress">Sequencer register selected by port 3C4h.</param>
/// <param name="Sequencer">Sequencer registers 0 to 4.</param>
/// <param name="CrtControllerAddress">CRT controller register selected by port 3D4h (or 3B4h).</param>
/// <param name="CrtController">CRT controller registers 0 to 18h.</param>
/// <param name="GraphicsControllerAddress">Graphics controller register selected by port 3CEh.</param>
/// <param name="GraphicsController">Graphics controller registers 0 to 8.</param>
/// <param name="AttributeControllerAddress">Attribute controller register selected by port 3C0h.</param>
/// <param name="AttributeController">Attribute controller registers 0 to 14h, palette entries included.</param>
/// <param name="IsAttributeDataNext">
///     Whether the next write to port 3C0h goes to the selected attribute register. Otherwise it selects a
///     register. A read of input status 1 clears it.
/// </param>
/// <param name="MiscellaneousOutput">Miscellaneous output register (ports 3C2h and 3CCh).</param>
/// <param name="InputStatus0">Input status 0 register (port 3C2h).</param>
/// <param name="InputStatus1">
///     Input status 1 register as last stored. A read of port 3DAh computes the retrace bits from the clock.
/// </param>
/// <param name="CgaBitsPerPixel">Bits per pixel of the CGA mode the video BIOS set, 0 outside of CGA modes.</param>
/// <param name="CgaModeControl">CGA mode control register (port 3D8h).</param>
/// <param name="CgaColorSelect">CGA color select register (port 3D9h).</param>
/// <param name="Dac">State of the DAC.</param>
public sealed record VgaRegistersState(
    byte SequencerAddress,
    ReadOnlyMemory<byte> Sequencer,
    byte CrtControllerAddress,
    ReadOnlyMemory<byte> CrtController,
    byte GraphicsControllerAddress,
    ReadOnlyMemory<byte> GraphicsController,
    byte AttributeControllerAddress,
    ReadOnlyMemory<byte> AttributeController,
    bool IsAttributeDataNext,
    byte MiscellaneousOutput,
    byte InputStatus0,
    byte InputStatus1,
    byte CgaBitsPerPixel,
    byte CgaModeControl,
    byte CgaColorSelect,
    DacState Dac);
