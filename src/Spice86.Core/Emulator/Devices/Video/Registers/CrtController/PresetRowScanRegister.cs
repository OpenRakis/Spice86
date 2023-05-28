namespace Spice86.Core.Emulator.Devices.Video.Registers.CrtController;

public class PresetRowScanRegister : Register8 {
    /// <summary>
    ///     This field specifies the scanline where the first character row begins. This provides scrolling on a scanline
    ///     basis (soft scrolling). The contents of this field should be changed only during Vertical Retrace time.
    /// </summary>
    public byte PresetRowScan {
        get => GetBits(4, 0);
        set => SetBits(4, 0, value);
    }

    /// <summary>
    ///     This two-bit field controls coarse panning. It can specify a pan of up to 24 pixels with a resolution of eight
    ///     pixels. AR13 provides for panning on a pixel basis.
    /// </summary>
    public byte BytePanning {
        get => GetBits(6, 5);
        set => SetBits(6, 5, value);
    }
}