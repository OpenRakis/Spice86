namespace Spice86.Core.Emulator.Devices.Video.Registers.Graphics;

public class MiscellaneousGraphicsRegister : Register8 {
    /// <summary>
    ///     If this bit is programmed to ‘1’, the CL-GD542X functions in Graphics (A.P.A.) modes. If this bit is programmed to
    ///     ‘0’,
    ///     the device functions in Text (A.N.) modes.
    /// </summary>
    public bool GraphicsMode {
        get => GetBit(0);
        set => SetBit(0, value);
    }

    /// <summary>
    ///     When this bit is programmed to ‘1’, CPU Address bit 0 is replaced with a higher-order address bit. This causes even
    ///     host addresses to access Planes 0 and 2, and odd host addresses to access Planes 1 and 3. This mode is useful for MDA
    ///     emulation.
    /// </summary>
    public bool ChainOddMapsToEven {
        get => GetBit(1);
        set => SetBit(1, value);
    }

    /// <summary>
    ///     This field specifies the beginning address and size of the display memory in the Host Address Space.
    ///     | Map | StartSegment | Length |
    ///     | 0   | 0xA000       | 128K   |
    ///     | 1   | 0xA000       |  64K   |
    ///     | 2   | 0xB000       |  32K   |
    ///     | 3   | 0xB800       |  32K   |
    /// </summary>
    public byte MemoryMap {
        get => GetBits(3, 2);
        set => SetBits(3, 2, value);
    }
    
    /// <summary>
    ///   The base address for the graphics memory window.
    /// </summary>
    public uint BaseAddress => MemoryMap switch {
        0 => 0xA0000,
        1 => 0xA0000,
        2 => 0xB0000,
        3 => 0xB8000,
        _ => throw new ArgumentOutOfRangeException()
    };
}