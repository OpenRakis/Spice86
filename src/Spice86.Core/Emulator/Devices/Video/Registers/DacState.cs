namespace Spice86.Core.Emulator.Devices.Video.Registers;

/// <summary>
///     Complete state of the video DAC, as captured by <see cref="DacRegisters.CaptureState" />.
/// </summary>
/// <remarks>
///     The ARGB maps the renderer reads are derived from the palette and the pixel mask on restore and are not
///     stored.
/// </remarks>
/// <param name="Palette">The 256 palette entries, three 6-bit components each (red, green, blue), 768 bytes.</param>
/// <param name="PixelMask">Pixel mask register (port 3C6h).</param>
/// <param name="IndexRegister">Value a read of port 3C8h returns, before it is cut to a byte.</param>
/// <param name="InternalIndex">Palette entry the next data access reads or writes.</param>
/// <param name="TripletCounter">Component (0 to 2) of that entry the next data access reads or writes.</param>
/// <param name="State">DAC state register (port 3C7h): 0 after a write access, 3 after a read access.</param>
/// <param name="TripletJustCompleted">Whether the last data write completed an entry.</param>
/// <param name="LastWrittenPaletteIndex">Last palette entry completed by a data write.</param>
public sealed record DacState(
    ReadOnlyMemory<byte> Palette,
    byte PixelMask,
    int IndexRegister,
    byte InternalIndex,
    int TripletCounter,
    byte State,
    bool TripletJustCompleted,
    byte LastWrittenPaletteIndex);
