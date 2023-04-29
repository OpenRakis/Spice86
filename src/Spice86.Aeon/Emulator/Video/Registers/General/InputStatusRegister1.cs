namespace Spice86.Aeon.Emulator.Video.Registers.General;

/// <summary>
/// The address for this read-only register is address hex 03DA or 03BA.
/// Do not write to this register.
/// </summary>
public class InputStatusRegister1 : VgaRegisterBase {

    /// <summary>
    /// When the Vertical Retrace field (bit 3) is 1, it indicates a vertical retrace interval. This bit can be programmed,
    /// through the Vertical Retrace End register, to generate an interrupt at the start of the vertical retrace.
    /// </summary>
    public bool VerticalRetrace {
        get => GetBit(3);
        set => SetBit(3, value);
    }

    /// <summary>
    /// When the Display Enable field (bit 0) is 1, it indicates a horizontal or vertical retrace interval. This bit is the
    /// real-time status of the inverted ‘display enable’ signal. In the past, programs have used this status bit to
    /// restrict screen updates to the inactive display intervals to reduce screen flicker. The video subsystem is
    /// designed to eliminate this software requirement; screen updates may be made at any time without screen
    /// degradation.
    /// </summary>
    public bool DisplayEnable {
        get => GetBit(0);
        set => SetBit(0, value);
    }
}