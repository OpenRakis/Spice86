namespace Spice86.Core.Emulator.Devices.Video.Registers.CrtController;

/// <summary>
/// Represents the 8 bit Underline Row Scanline register. Used to set the location of the underline in text mode.
/// </summary>
public class UnderlineRowScanlineRegister : Register8 {
    /// <summary>
    ///     When this bit is set to true, doubleWord addresses are forced. The CRTC Memory Address Counter is rotated
    ///     left two bit positions, so that Display Memory Address bits 1 and 0 are sourced from CRTC Address Counter bits
    ///     13 and 12, respectively. When this bit is set to false, CR17[6] controls whether the chip uses byte or word
    ///     addresses.
    /// </summary>
    public bool DoubleWordMode {
        get => GetBit(6);
        set => SetBit(6, value);
    }

    /// <summary>
    ///     This bit must be set to true when DoubleWord mode is enabled, to clock the Memory Address Counter with the
    ///     character clock divided by four. This bit must be set to false when DoubleWord mode is not enabled.
    /// </summary>
    public bool CountByFour {
        get => GetBit(5);
        set => SetBit(5, value);
    }

    /// <summary>
    /// These bits specify the horizontal scan line of a character row on which an underline occurs. The value programmed is the scan line desired minus 1.
    /// </summary>
    public byte UnderlineScanline {
        get => GetBits(4, 0);
        set => SetBits(4, 0, value);
    }
}