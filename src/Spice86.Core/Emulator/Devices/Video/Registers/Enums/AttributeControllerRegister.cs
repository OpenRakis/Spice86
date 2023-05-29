namespace Spice86.Core.Emulator.Devices.Video.Registers.Enums;

/// <summary>
///     Names of the attribute controller registers.
/// </summary>
public enum AttributeControllerRegister {
    /// <summary>
    ///     First palette entry.
    /// </summary>
    FirstPaletteEntry,

    /// <summary>
    ///     Last palette entry.
    /// </summary>
    LastPaletteEntry = 0x0F,

    /// <summary>
    ///     Attribute Mode Control register.
    /// </summary>
    AttributeModeControl,

    /// <summary>
    ///     Overscan Color register LUT index.
    /// </summary>
    OverscanColor,

    /// <summary>
    ///     Color Plane Enable register.
    /// </summary>
    ColorPlaneEnable,

    /// <summary>
    ///     Horizontal Pixel Panning register.
    /// </summary>
    HorizontalPixelPanning,

    /// <summary>
    ///     Color Select register.
    /// </summary>
    ColorSelect
}