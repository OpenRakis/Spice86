namespace Spice86.Core.Emulator.Devices.Video.Registers.Enums;

public enum AttributeControllerRegister {
    FirstPaletteEntry,
    LastPaletteEntry = 0x0F,
    AttributeModeControl,
    OverscanColor,
    ColorPlaneEnable,
    HorizontalPixelPanning,
    ColorSelect
}