namespace Spice86.Core.Emulator.Devices.Video.Registers.CrtController;

public class CrtModeControlRegister : Register8 {
    /// <summary>
    ///     CRTC Timing Logic enabled/disabled
    /// </summary>
    public bool TimingEnable {
        get => GetBit(7);
        set => SetBit(7, value);
    }

    /// <summary>
    ///     If this bit is set to true, the contents of the CRTC Address Counter are sent to the display memory without
    ///     being rotated. If this bit is set to false, the contents of the CRTC Address Counter are rotated left one bit
    ///     position before being sent to display memory.
    /// </summary>
    public ByteWordMode ByteWordMode {
        get => GetBit(6) ? ByteWordMode.Byte : ByteWordMode.Word;
        set => SetBit(6, value == ByteWordMode.Byte);
    }

    /// <summary>
    ///     In 'word mode', this bit controls whether the chip uses byte or word addresses. If this bit is set to true, the
    ///     rotation involves 16 bits, otherwise 14 bits. In 'byte mode', this bit is ignored.
    /// </summary>
    public bool AddressWrap {
        get => GetBit(5);
        set => SetBit(5, value);
    }

    /// <summary>
    ///     False: Memory Address Counter is incremented every character clock.
    ///     True: Memory Address Counter is incremented every 2 character clocks.
    /// </summary>
    public bool CountByTwo {
        get => GetBit(3);
        set => SetBit(3, value);
    }

    /// <summary>
    ///     False: Scan line counter is incremented every Hsync.
    ///     True: Scan line counter is incremented every 2 Hsyncs.
    /// </summary>
    public bool VerticalTimingHalved {
        get => GetBit(2);
        set => SetBit(2, value);
    }

    /// <summary>
    ///     False: Substitute character row scan line counter bit 1 for memory address bit 14 during active display time
    ///     True: normal operation, no substitution takes place.
    /// </summary>
    public bool SelectRowScanCounter {
        get => GetBit(1);
        set => SetBit(1, value);
    }

    /// <summary>
    ///     False: Substitute character row scan line counter bit 0 for memory address bit 13 during active display time
    ///     True: normal operation, no substitution takes place.
    /// </summary>
    public bool CompatibilityModeSupport {
        get => GetBit(0);
        set => SetBit(0, value);
    }
}

public enum ByteWordMode {
    Word,
    Byte
}