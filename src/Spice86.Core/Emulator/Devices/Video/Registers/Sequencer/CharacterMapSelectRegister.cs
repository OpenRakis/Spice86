namespace Spice86.Core.Emulator.Devices.Video.Registers.Sequencer;

/// <summary>
///     This register has an index of hex 03; its address is hex 03C5. In alphanumeric modes, bit 3 of the attribute byte
///     normally defines
///     the foreground intensity. This bit can be redefined as a switch between character sets, allowing 512 displayable
///     characters. To
///     enable this feature:
///     1. Set the extended memory bit in the Memory Mode register (index hex 04) to 1.
///     2. Select different values for character map A and character map B.
///     This function is supported by BIOS and is a function call within the character generator routines.
/// </summary>
public class CharacterMapSelectRegister : Register8 {
    /// <summary>
    ///     Map A is the area of plane 2 containing the character font table used to generate characters when attribute bit 3
    ///     is set to 1.
    /// </summary>
    public int CharacterMapA {
        get => (Value & 0x20) >> 3 | (Value & 0xC) >> 2;
        set => Value = (byte)(Value & 0b00010011 | (value & 0x03) << 2 | (value & 0x04) << 3);
    }

    /// <summary>
    ///     Map B is the area of plane 2 containing the character font table used to generate characters when attribute bit 3
    ///     is set to 0.
    /// </summary>
    public int CharacterMapB {
        get => (Value & 0x10) >> 2 | Value & 0x3;
        set => Value = (byte)(Value & 0b00101100 | value & 0x03 | (value & 0x04) << 2);
    }
}