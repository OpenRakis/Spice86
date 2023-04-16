namespace Spice86.Aeon.Emulator.Video.Registers.General;

/// <summary>
/// The address for this read-only register is address hex 03C2.
/// Do not write to this register.
/// </summary>
public class InputStatusRegister0 {
    public byte Value { get; set; }

    /// <summary>
    /// When the CRT Interrupt field (bit 7) is 1, a vertical retrace interrupt is pending.
    /// </summary>
    public bool CrtInterrupt {
        get => (Value & 0x80) != 0;
        set => Value = (byte)(Value & 0x7F | (value ? 0x80 : 0x00));
    }

    /// <summary>
    /// BIOS uses the Switch Sense field (bit 4) in determining the type of display attached.
    /// </summary>
    public bool SwitchSense {
        get => (Value & 0x10) != 0;
        set => Value = (byte)(Value & 0xEF | (value ? 0x10 : 0x00));
    }
}