namespace Spice86.Core.Emulator.InterruptHandlers.VGA.Enums;

/// <summary>
/// VBE controller capabilities flags.
/// </summary>
[Flags]
public enum VbeCapabilities : uint {
    /// <summary>
    /// DAC is switchable to 8-bit mode.
    /// </summary>
    Dac8BitCapable = 0x00000001,

    /// <summary>
    /// Controller is not VGA compatible.
    /// </summary>
    NonVgaController = 0x00000002,

    /// <summary>
    /// RAMDAC operation requires blank bit set when loading color values.
    /// </summary>
    RamdacRequiresBlankBit = 0x00000004
}
