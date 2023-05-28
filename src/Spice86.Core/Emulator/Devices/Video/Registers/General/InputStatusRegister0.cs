namespace Spice86.Core.Emulator.Devices.Video.Registers.General;

/// <summary>
///     The address for this read-only register is address hex 03C2.
///     Do not write to this register.
/// </summary>
public class InputStatusRegister0 : Register8 {
    /// <summary>
    ///     When the CRT Interrupt field (bit 7) is 1, a vertical retrace interrupt is pending.
    /// </summary>
    public bool CrtInterrupt {
        get => GetBit(7);
        set => SetBit(7, value);
    }

    /// <summary>
    ///     BIOS uses the Switch Sense field (bit 4) in determining the type of display attached.
    /// </summary>
    public bool SwitchSense {
        get => GetBit(4);
        set => SetBit(4, value);
    }
}