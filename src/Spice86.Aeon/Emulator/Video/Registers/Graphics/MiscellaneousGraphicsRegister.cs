namespace Spice86.Aeon.Emulator.Video.Registers.Graphics;

public class MiscellaneousGraphicsRegister {
    public byte Value { get; set; } = 0b0010;

    /// <summary>
    /// If this bit is programmed to ‘1’, the CL-GD542X functions in Graphics (A.P.A.) modes. If this bit is programmed to ‘0’,
    /// the device functions in Text (A.N.) modes.
    /// </summary>
    public bool GraphicsMode {
        get => (Value & 0x01) != 0;
        set => Value = (byte)(Value & ~0x01 | (value ? 0x01 : 0));
    }

    /// <summary>
    /// When this bit is programmed to ‘1’, CPU Address bit 0 is replaced with a higher-order address bit. This causes even host
    /// addresses to access Planes 0 and 2, and odd host addresses to access Planes 1 and 3. This mode is useful for MDA emulation.
    /// </summary>
    public bool ChainOddMapsToEven {
        get => (Value & 0x02) != 0;
        set => Value = (byte)(Value & ~0x02 | (value ? 0x02 : 0));
    }

    /// <summary>
    /// This field specifies the beginning address and size of the display memory in the Host Address Space.
    /// | Map | StartSegment | Length |  
    /// | 0   | 0xA000       | 128K   |
    /// | 1   | 0xA000       |  64K   |
    /// | 2   | 0xB000       |  32K   |
    /// | 3   | 0xB800       |  32K   |
    /// </summary>
    public int MemoryMap {
        get => (Value & 0b00001100) >> 2;
        set => Value = (byte)(Value & 0b11110011 | (value & 0x03) << 2);
    }
}