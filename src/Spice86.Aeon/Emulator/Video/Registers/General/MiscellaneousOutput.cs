namespace Spice86.Aeon.Emulator.Video.Registers.General;

public class MiscellaneousOutput : VgaRegisterBase {
    /// <summary>
    /// The I/O Address Select field (bit 0) selects the CRT controller addresses. When set to 0, this bit sets the
    /// CRT controller addresses to hex 03Bx and the address for the Input Status Register 1 to hex 03BA for
    /// compatibility with the monochrome adapter. When set to 1, this bit sets CRT controller addresses to hex 03Dx
    /// and the Input Status Register 1 address to hex 03DA for compatibility with the color/graphics adapter. The write
    /// addresses to the Feature Control register are affected in the same manner.
    /// </summary>
    public IoAddressSelect IoAddressSelect {
        get => GetBit(0) ? IoAddressSelect.Color : IoAddressSelect.Monochrome;
        set => SetBit(0, value == IoAddressSelect.Color);
    }

    /// <summary>
    /// When set to 0, the Enable RAM field (bit 1) disables address decode for the display buffer from the system
    /// </summary>
    public bool EnableRam {
        get => GetBit(1);
        set => SetBit(1, value);
    }

    /// <summary>
    /// The Clock Select field (bits 3, 2) selects the clock source according to the following figure. The external
    /// /// clock is driven through the auxiliary video extension. The input clock should be kept between 14.3 MHz and
    /// 28.4 MHz.
    /// </summary>
    public ClockSelect ClockSelect {
        get => (ClockSelect)GetBits(3, 2);
        set => SetBits(3, 2, (byte)value);
    }

    /// <summary>
    /// This bit affects the meaning of the LSB of display memory address when in Even/Odd modes (SR4[2] = 1). If this bit is programmed to ‘0’,
    /// only odd memory locations are selected. If this bit is programmed to ‘1’, only even memory locations are selected.
    /// </summary>
    public bool OddPageSelect {
        get => GetBit(5);
        set => SetBit(5, value);
    }

    /// <summary>
    /// When set to 0, the Horizontal Sync Polarity field (bit 6) selects a positive ‘horizontal retrace’ signal. Bits 7 and
    /// 6 select the vertical size
    /// </summary>
    public Polarity HorizontalSyncPolarity {
        get => GetBit(6) ? Polarity.Negative : Polarity.Positive;
        set => SetBit(6, value == Polarity.Negative);
    }

    /// <summary>
    /// When set to 0, the Vertical Sync Polarity field (bit 7) selects a positive ‘vertical retrace’ signal. This bit
    /// works with bit 6 to determine the vertical size.
    /// </summary>
    public Polarity VerticalSyncPolarity {
        get => GetBit(7) ? Polarity.Negative : Polarity.Positive;
        set => SetBit(7, value == Polarity.Negative);
    }

    public int VerticalSize => HorizontalSyncPolarity switch {
        Polarity.Negative when VerticalSyncPolarity == Polarity.Positive => 400,
        Polarity.Positive when VerticalSyncPolarity == Polarity.Negative => 350,
        Polarity.Negative when VerticalSyncPolarity == Polarity.Negative => 480,
        _ => 0
    };
}