namespace Spice86.Core.Emulator.InterruptHandlers.VGA.Enums;

/// <summary>
/// VBE mode attribute flags that describe the capabilities and characteristics of a video mode.
/// These flags are stored in the ModeAttributes field of the ModeInfoBlock structure.
/// </summary>
[Flags]
public enum VbeModeAttribute : ushort {
    /// <summary>
    /// Mode is supported in hardware.
    /// </summary>
    ModeSupported = 0x0001,

    /// <summary>
    /// TTY output functions are supported by the BIOS.
    /// </summary>
    TtyOutputSupported = 0x0004,

    /// <summary>
    /// Mode is a color mode (as opposed to monochrome).
    /// </summary>
    ColorMode = 0x0008,

    /// <summary>
    /// Mode is a graphics mode (as opposed to text mode).
    /// </summary>
    GraphicsMode = 0x0010,

    /// <summary>
    /// Mode is not VGA compatible.
    /// </summary>
    NonVgaMode = 0x0020,

    /// <summary>
    /// Windowed frame buffer mode is not available.
    /// </summary>
    NoWindowedFrameBuffer = 0x0040,

    /// <summary>
    /// Linear frame buffer mode is available.
    /// </summary>
    LinearFrameBufferAvailable = 0x0080
}
