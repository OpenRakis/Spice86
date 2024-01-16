namespace Spice86.Core.Emulator.Devices.Video.Registers;

using Spice86.Core.Emulator.Devices.Video.Registers.AttributeController;
using Spice86.Core.Emulator.Devices.Video.Registers.Enums;

/// <summary>
///     Emulates the VGA Attribute Controller registers.
/// </summary>
public sealed class AttributeControllerRegisters {
    /// <summary>
    ///     Initializes a new instance of the <see cref="AttributeControllerRegisters" /> class.
    /// </summary>
    public AttributeControllerRegisters() {
        InternalPalette = new byte[16];
    }

    /// <summary>
    /// Gets or sets the address register.
    /// </summary>
    public AttributeControllerRegister AddressRegister { get; set; }

    /// <summary>
    ///     Gets the internal palette.
    /// </summary>
    public byte[] InternalPalette { get; set; }

    /// <summary>
    ///     Gets or sets the Attribute Mode Control register.
    /// </summary>
    public AttributeControllerModeRegister AttributeControllerModeRegister { get; } = new();

    /// <summary>
    ///     Gets or sets the Overscan Color register LUT index.
    /// </summary>
    public byte OverscanColor { get; set; }

    /// <summary>
    ///     Gets or sets the Color Plane Enable register.
    /// </summary>
    public ColorPlaneEnableRegister ColorPlaneEnableRegister { get; } = new();

    /// <summary>
    ///     Gets or sets the Horizontal Pixel Panning register.
    /// </summary>
    public byte HorizontalPixelPanning { get; set; }

    /// <summary>
    ///     Gets or sets the Color Select register.
    /// </summary>
    public ColorSelectRegister ColorSelectRegister { get; set; } = new();

    /// <summary>
    ///     Returns the current value of an attribute controller register.
    /// </summary>
    /// <param name="register">Address of register to read.</param>
    /// <returns>Current value of the register.</returns>
    public byte ReadRegister(AttributeControllerRegister register) {
        if (register is >= AttributeControllerRegister.FirstPaletteEntry and <= AttributeControllerRegister.LastPaletteEntry) {
            return InternalPalette[(byte)register];
        }

        return register switch {
            AttributeControllerRegister.AttributeModeControl => AttributeControllerModeRegister.Value,
            AttributeControllerRegister.OverscanColor => OverscanColor,
            AttributeControllerRegister.ColorPlaneEnable => ColorPlaneEnableRegister.Value,
            AttributeControllerRegister.HorizontalPixelPanning => HorizontalPixelPanning,
            AttributeControllerRegister.ColorSelect => ColorSelectRegister.Value,
            _ => 0
        };
    }

    /// <summary>
    ///     Writes to an attribute controller register.
    /// </summary>
    /// <param name="register">Address of register to write.</param>
    /// <param name="value">Value to write to register.</param>
    public void WriteRegister(AttributeControllerRegister register, byte value) {
        if (register is >= AttributeControllerRegister.FirstPaletteEntry and <= AttributeControllerRegister.LastPaletteEntry) {
            InternalPalette[(byte)register] = (byte)(value & 0x3F);
        } else {
            switch (register) {
                case AttributeControllerRegister.AttributeModeControl:
                    AttributeControllerModeRegister.Value = value;
                    break;

                case AttributeControllerRegister.OverscanColor:
                    OverscanColor = value;
                    break;

                case AttributeControllerRegister.ColorPlaneEnable:
                    ColorPlaneEnableRegister.Value = value;
                    break;

                case AttributeControllerRegister.HorizontalPixelPanning:
                    HorizontalPixelPanning = value;
                    break;

                case AttributeControllerRegister.ColorSelect:
                    ColorSelectRegister.Value = value;
                    break;
            }
        }
    }
}