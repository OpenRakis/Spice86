namespace Spice86.Core.Emulator.Devices.Video.Registers;

using Spice86.Core.Emulator.Devices.Video.Registers.General;

/// <summary>
/// Represents the general registers of the VGA.
/// </summary>
public class GeneralRegisters {
    /// <summary>
    /// Gets or sets the BIOS-selected CGA graphics depth: zero outside CGA modes, otherwise one or two.
    /// </summary>
    public byte CgaBitsPerPixel { get; set; }

    /// <summary>
    /// Gets or sets the legacy CGA mode-control latch at port 03D8h.
    /// </summary>
    public byte CgaModeControl { get; set; }

    /// <summary>
    /// Gets or sets the legacy CGA color-select latch at port 03D9h.
    /// </summary>
    public byte CgaColorSelect { get; set; }

    /// <summary>
    /// Gets whether CGA graphics output is enabled without stopping display timing.
    /// </summary>
    public bool CgaVideoEnabled => CgaBitsPerPixel == 0 || (CgaModeControl & 0x08) != 0;

    /// <summary>
    ///     Get the Miscellaneous Output register.
    /// </summary>
    public MiscellaneousOutput MiscellaneousOutput { get; init; } = new();

    /// <summary>
    ///     Get the Input Status Register 0.
    /// </summary>
    public InputStatusRegister0 InputStatusRegister0 { get; init; } = new();

    /// <summary>
    ///     Get the Input Status Register 1.
    /// </summary>
    public InputStatusRegister1 InputStatusRegister1 { get; init; } = new();
}