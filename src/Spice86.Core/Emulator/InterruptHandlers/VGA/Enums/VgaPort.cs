namespace Spice86.Core.Emulator.InterruptHandlers.VGA.Enums;
/// <summary>
/// Enum representing the various VGA ports.
/// <see href="https://wiki.osdev.org/VGA_Hardware"/>
/// </summary>
public enum VgaPort {
    /// <summary>
    /// CRT Controller Address Register.
    /// </summary>
    CrtControllerAddress = 0x3B4,

    /// <summary>
    /// Attribute Address Register.
    /// </summary>
    AttributeAddress = 0x3C0,

    /// <summary>
    /// Attribute Data Register.
    /// </summary>
    AttributeData = 0x3C1,

    /// <summary>
    /// Miscellaneous Output Register (write mode).
    /// </summary>
    MiscOutputWrite = 0x3C2,

    /// <summary>
    /// Sequencer Address Register.
    /// </summary>
    SequencerAddress = 0x3C4,

    /// <summary>
    /// DAC Pixel Mask Register.
    /// </summary>
    DacPelMask = 0x3C6,

    /// <summary>
    /// DAC Address Read Index Register.
    /// </summary>
    DacAddressReadIndex = 0x3C7,

    /// <summary>
    /// DAC Address Write Index Register.
    /// </summary>
    DacAddressWriteIndex = 0x3C8,

    /// <summary>
    /// DAC Data Register.
    /// </summary>
    DacData = 0x3C9,

    /// <summary>
    /// Miscellaneous Output Register (read mode).
    /// </summary>
    MiscOutputRead = 0x3CC,

    /// <summary>
    /// Graphics Controller Address Register.
    /// </summary>
    GraphicsControllerAddress = 0x3CE,

    /// <summary>
    /// Alternate CRT Controller Address Register.
    /// </summary>
    CrtControllerAddressAlt = 0x3D4,

    /// <summary>
    /// Alternate Input Status 1 Register (read mode).
    /// </summary>
    InputStatus1ReadAlt = 0x3DA
}
