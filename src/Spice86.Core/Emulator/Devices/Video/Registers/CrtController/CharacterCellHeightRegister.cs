namespace Spice86.Core.Emulator.Devices.Video.Registers.CrtController;

/// <summary>
/// Represents the 8 bit Character Cell Height register.
/// </summary>
public class CharacterCellHeightRegister : Register8 {
    /// <summary>
    ///     If this bit is programmed to ‘1’, every scanline is displayed twice in succession. The Scanline Counter-based
    ///     addressing (Character Height, Cursor Start and End, and Underline location) double.
    /// </summary>
    public bool CrtcScanDouble {
        get => GetBit(7);
        set => SetBit(7, value);
    }

    /// <summary>
    ///     This bit extends the Line Compare field (CR18) to ten bits.
    /// </summary>
    public int LineCompare9 => GetBit(6) ? 1 << 9 : 0;

    /// <summary>
    ///     This bit extends the Vertical Blank Start field (CR15) to ten bits
    /// </summary>
    public int VerticalBlankStart9 => GetBit(5) ? 1 << 9 : 0;

    /// <summary>
    ///     This field specifies the vertical size of the character cell in terms of scan lines. The value programmed into
    ///     this field is the actual size minus 1.
    /// </summary>
    public byte MaximumScanline {
        get => GetBits(4, 0);
        set => SetBits(4, 0, value);
    }
}