namespace Spice86.Aeon.Emulator.Video.Registers.General;

public class MiscellaneousOutput {
    public byte Value { get; set; }

    /// <summary>
    /// The I/O Address Select field (bit 0) selects the CRT controller addresses. When set to 0, this bit sets the
    /// CRT controller addresses to hex 03Bx and the address for the Input Status Register 1 to hex 03BA for
    /// compatibility with the monochrome adapter. When set to 1, this bit sets CRT controller addresses to hex 03Dx
    /// and the Input Status Register 1 address to hex 03DA for compatibility with the color/graphics adapter. The write
    /// addresses to the Feature Control register are affected in the same manner.
    /// </summary>
    public IoAddressSelect IoAddressSelect {
        get => (Value & 0x01) != 0 ? IoAddressSelect.Color : IoAddressSelect.Monochrome;
        set => Value = (byte)(Value & 0xFE | (value == IoAddressSelect.Color ? 0x01 : 0x00));
    }

    /// <summary>
    /// When set to 0, the Enable RAM field (bit 1) disables address decode for the display buffer from the system
    /// </summary>
    public bool EnableRam {
        get => (Value & 0x02) != 0;
        set => Value = (byte)(Value & 0xFD | (value ? 0x02 : 0x00));
    }

    /// <summary>
    /// The Clock Select field (bits 3, 2) selects the clock source according to the following figure. The external
    /// /// clock is driven through the auxiliary video extension. The input clock should be kept between 14.3 MHz and
    /// 28.4 MHz.
    /// </summary>
    public ClockSelect ClockSelect {
        get => (ClockSelect)((Value & 0x0C) >> 2);
        set => Value = (byte)(Value & 0xF3 | (int)value << 2);
    }

    /// <summary>
    /// When set to 0, the Horizontal Sync Polarity field (bit 6) selects a positive ‘horizontal retrace’ signal. Bits 7 and
    /// 6 select the vertical size
    /// </summary>
    public Polarity HorizontalSyncPolarity {
        get => (Value & 0x40) != 0 ? Polarity.Positive : Polarity.Negative;
        set => Value = (byte)(Value & 0xBF | (value == Polarity.Positive ? 0x40 : 0x00));
    }

    /// <summary>
    /// When set to 0, the Vertical Sync Polarity field (bit 7) selects a positive ‘vertical retrace’ signal. This bit
    /// works with bit 6 to determine the vertical size.
    /// </summary>
    public Polarity VerticalSyncPolarity {
        get => (Value & 0x80) != 0 ? Polarity.Positive : Polarity.Negative;
        set => Value = (byte)(Value & 0x7F | (value == Polarity.Positive ? 0x80 : 0x00));
    }

    public int VerticalSize => HorizontalSyncPolarity switch {
        Polarity.Positive when VerticalSyncPolarity == Polarity.Negative => 400,
        Polarity.Negative when VerticalSyncPolarity == Polarity.Positive => 350,
        Polarity.Positive when VerticalSyncPolarity == Polarity.Positive => 480,
        _ => 0
    };

}