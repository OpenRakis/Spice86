namespace Spice86.Aeon.Emulator.Video.Registers.Graphics;

public class GraphicsModeRegister {
    public byte Value { get; set; }

    /// <summary>
    /// Write Mode 0:
    ///   Each of the four display memory planes is written with the CPU data rotated by the number of counts in GR3[2:0]. If a bit in GR1[3:0] is
    ///   programmed to ‘1’, the corresponding plane is written with the contents of the corresponding bit in GR0[3:0]. The contents of the data latches may
    ///   be combined with the data from the SR logic under control of GR3[4:3]. Bit planes are enabled with SR2[3:0]. Bit positions are enabled with GR8.
    /// Write Mode 1:
    ///   Each of the four display memory planes is written with the data in the Data Latches. The Data Latches had been loaded from display memory
    ///   with a previous read. GR8 is ignored in Write mode 1.
    /// Write Mode 2:
    ///   Display memory planes 3:0 are written with value of Data bits 3:0, respectively. The four bits are replicated eight times each to write up to
    ///   eight adjacent pixels. Bit planes are enabled with SR2[3:0]. Bit positions are enabled with GR8. The Data Rotator, SR logic, and Function
    ///   Select fields are ignored in Write mode 2.
    /// Write Mode 3:
    ///   The data for each display memory plane comes from the corresponding bit of GR0[3:0]. The bit-position-enable field is formed with the
    ///   logical AND of GR8 and the rotated CPU data. The SR and Function Select fields are ignored in Write mode 3.
    /// </summary>
    public WriteMode WriteMode {
        get => (WriteMode)(Value & 0x7);
        set => Value = (byte)(Value & ~0x7 | (int)value & 0x7);
    }

    /// <summary>
    /// Read Mode 0:
    ///   If this bit is programmed to ‘0’, the CPU reads data directly from display memory. Each read returns eight adjacent bits of the display
    ///   memory plane specified in GR4[1:0]. The color-match logic is not used in Read mode 0. Note that an I/O read of CR22 forces a Read mode 0 operation.
    /// Read Mode 1:
    ///   If this bit is programmed to ‘1’, the CPU reads the results of the color compare logic. Read mode 1 allows eight adjacent pixels (16-color modes)
    ///   to be compared to a specified color value in a single operation. Each of the eight bits returned to the processor indicates the result of a compare
    ///   between the four bits of the Color Compare (GR2[3:0]) and the bits from the four display memory planes. If the four bits of the Color Compare match the
    ///   four bits from the display memory planes, ‘1’ is returned for the corresponding bit position. If any bits in the Color Don’t Care (GR7[3:0]) are zeroes,
    ///   the corresponding plane comparison is forced to match.
    /// </summary>
    public ReadMode ReadMode {
        get => (Value & 0x08) == 0 ? ReadMode.ReadMode0 : ReadMode.ReadMode1;
        set => Value = (byte)(value == ReadMode.ReadMode0 ? Value & ~0x08 : Value | 0x08);
    }

    /// <summary>
    /// If this bit is programmed to ‘1’, the Graphics Controller is configured for Odd/Even Addressing mode. This bit should always be programmed
    /// to the opposite value as Spice86.Aeon.Emulator.Video.Registers.Sequencer.MemoryModeRegister.OddEvenMode.
    /// </summary>
    public bool OddEven {
        get => (Value & 0x10) != 0;
        set => Value = (byte)(value ? Value | 0x10 : Value & ~0x10);
    }

    /// <summary>
    /// If this bit is programmed to ‘1’, the Video Shift registers are configured for CGA compatibility. This is used for Video modes 4 and 5. If
    /// this bit is programmed to ‘0’, the Video Shift registers are configured for EGA compatibility.
    /// </summary>
    public ShiftRegisterMode ShiftRegisterMode {
        get => (Value & 0x20) == 0 ? ShiftRegisterMode.Cga : ShiftRegisterMode.Ega;
        set => Value = (byte)(value == ShiftRegisterMode.Cga ? Value & ~0x20 : Value | 0x20);
    }

    /// <summary>
    ///  If this bit is programmed to ‘1’, the Video Shift registers are configured for 256-color Video modes. GR5[5] is ignored. If this bit is
    ///  programmed to ‘0’, the Video Shift registers are configured for 16-, 4-, or 2-color modes.
    /// </summary>
    public bool In256ColorMode {
        get => (Value & 0x40) != 0;
        set => Value = (byte)(value ? Value | 0x40 : Value & ~0x40);
    }
}

public enum ShiftRegisterMode {
    Cga,
    Ega
}

public enum WriteMode {
    WriteMode0,
    WriteMode1,
    WriteMode2,
    WriteMode3,
}

public enum ReadMode {
    ReadMode0,
    ReadMode1
}