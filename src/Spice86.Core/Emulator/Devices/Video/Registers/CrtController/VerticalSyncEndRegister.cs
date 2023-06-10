namespace Spice86.Core.Emulator.Devices.Video.Registers.CrtController;

public class VerticalSyncEndRegister : Register8 {
    /// <summary>
    ///     If this bit is set to true, registers CR0–CR7 cannot be written. Writes addressed to those registers are ignored.
    /// </summary>
    public bool WriteProtect {
        get => GetBit(7);
        set => SetBit(7, value);
    }

    /// <summary>
    ///     If this bit is set to true, five refresh cycles are executed per scanline. If this bit is set to
    ///     false, three  refresh cycles are executed per scanline.
    /// </summary>
    public bool RefreshCycleControl {
        get => GetBit(6);
        set => SetBit(6, value);
    }

    public byte RefreshCyclesPerScanline => (byte)(RefreshCycleControl ? 5 : 3);

    /// <summary>
    ///     If this bit is set d to true, the vertical interrupt is disabled. In this case, the Interrupt Request pin can
    ///     never go active. If this bit is set to false, the vertical interrupt is enabled and functions normally.
    /// </summary>
    public bool DisableVerticalInterrupt {
        get => GetBit(5);
        set => SetBit(5, value);
    }

    /// <summary>
    ///     If this bit is set to false, the Interrupt Pending bit (FEAT[7]) is cleared to ‘0’ and the Interrupt  Request
    ///     pin is forced inactive. Setting this bit to true allows the next occurence of the interrupt. This may be done
    ///     immediately after setting it to false.
    /// </summary>
    public bool ClearVerticalInterrupt {
        get => GetBit(4);
        set => SetBit(4, value);
    }

    /// <summary>
    ///     This field determines the width of the Vertical Sync pulse. The least-significant four bits of the Scanline
    ///     Counter are compared with the contents of this field. When a match occurs, the Vertical Sync pulse is ended.
    ///     Note the Vertical Sync pulse is limited to 15 scan lines.
    /// </summary>
    public byte VerticalSyncEnd {
        get => GetBits(3, 0);
        set => SetBits(3, 0, value);
    }
}