namespace Spice86.Aeon.Emulator.Video.Registers.CrtController;

public class HorizontalBlankingEndRegister : VgaRegisterBase {
    /// <summary>
    /// If this bit is programmed to ‘0’, registers CR10 and CR11 are write-only registers. If this bit is programmed
    /// to ‘1’, registers CR10 and CR11 are read/write registers.
    /// </summary>
    public bool CompatibleRead {
        get => GetBit(7);
        set => SetBit(7, value);
    }

    /// <summary>
    /// This two-bit field is used to specify the number of character clocks that display enable is to be delayed from
    /// Horizontal Total. This is necessary to compensate for the accesses of the character code and Attribute byte,
    /// the accesses of the font, etc
    /// </summary>
    public byte DisplayEnableSkew {
        get => GetBits(6, 5);
        set => SetBits(6, 5, value);
    }

    /// <summary>
    /// This field determines the width of the Horizontal Blanking Period. This field is extended with CR5[7]. The
    /// least-significant five or six bits of the Character Counter are compared with the contents of this field.
    /// When a match occurs, the Horizontal Blanking Period is ended. Note that the Horizontal Blanking Period is
    /// limited to 63 character-clock times. The value to be programmed into this register can be calculated by
    /// subtracting the desired blanking period from the value programmed into CR2 (Horizontal Blanking Start).
    /// Never program the blanking period to extend past the Horizontal Total.
    /// </summary>
    public byte HorizontalBlankingEnd {
        get => GetBits(4, 0);
        set => SetBits(4, 0, value);
    }
}