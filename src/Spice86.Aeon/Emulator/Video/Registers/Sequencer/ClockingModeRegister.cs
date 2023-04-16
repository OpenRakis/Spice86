namespace Spice86.Aeon.Emulator.Video.Registers.Sequencer;

/// <summary>
/// This read/write register has an index of hex 01; its address is hex 03C5.
/// </summary>
public class ClockingModeRegister {
    public byte Value { get; set; } = 0b000011;
        
    /// <summary>
    /// When set to 0, the 8/9 Dot Clocks field (bit 0) directs the sequencer to generate character clocks 9 dots wide;
    /// when set to 1, it directs the sequencer to generate character clocks 8 dots wide. The 9-dot mode is for
    /// alphanumeric modes 0 + , 1 + , 2 + , 3 + , 7, and 7 + only; the 9th dot equals the 8th dot for ASCII codes hex C0
    /// through DF. All other modes must use 8 dots per character clock.
    /// </summary>
    public int DotsPerClock {
        get => (Value & 0x1) != 0 ? 8 : 9;
        set => Value = (byte)(Value & 0xFE | (value == 8 ? 0x1 : 0));
    }

    /// <summary>
    /// When the Shift Load field (bit 2) and Shift 4 field (bit 4) are set to 0, the video serializers are loaded every
    /// character clock. When the Shift Load field (bit 2) is set to 1, the video serializers are loaded every other
    /// character clock, which is useful when 16 bits are fetched per cycle and chained together in the shift
    /// registers
    /// </summary>
    public bool ShiftLoad {
        get => (Value & 0x4) != 0;
        set => Value = (byte)(Value & 0xFB | (value ? 0x4 : 0));
    }

    /// <summary>
    /// When set to 0, the Dot Clock field (bit 3) selects the normal dot clocks derived from the sequencer master
    /// clock input. When set to 1, the master clock is divided by 2 to generate the dot clock. All other timings are
    /// affected because they are derived from the dot clock. The dot clock divided by 2 is used for 320 and 360
    /// horizontal PEL modes.
    /// </summary>
    public bool DotClock {
        get => (Value & 0x8) != 0;
        set => Value = (byte)(Value & 0xF7 | (value ? 0x8 : 0));
    }

    /// <summary>
    /// When the Shift 4 field (bit 4) and Shift Load field (bit 2) are set to 0, the video serializers are loaded every
    /// character clock. When the Shift 4 field is set to 1, the video serializers are loaded every fourth character
    /// clock, which is useful when 32 bits are fetched per cycle and chained together in the shift registers.
    /// </summary>
    public bool Shift4 {
        get => (Value & 0x10) != 0;
        set => Value = (byte)(Value & 0xEF | (value ? 0x10 : 0));
    }

    /// <summary>
    /// When set to 1, the Screen Off field (bit 5) turns off the display and assigns maximum memory bandwidth to
    /// the system. Although the display is blanked, the synchronization pulses are maintained. This bit can be
    /// used for rapid full-screen updates.
    /// </summary>
    public bool ScreenOff {
        get => (Value & 0x20) != 0;
        set => Value = (byte)(Value & 0xDF | (value ? 0x20 : 0));
    }
        
}