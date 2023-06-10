namespace Spice86.Core.Emulator.InterruptHandlers.VGA.Enums;

/// <summary>
///     Vga Mode setup flags.
/// </summary>
[Flags]
public enum ModeFlags {
    /// <summary>
    ///     Unknown.
    /// </summary>
    Legacy = 0x0001,

    /// <summary>
    ///     Indicates that colors need to be converted to grayscale.
    /// </summary>
    GraySum = 0x0002,

    /// <summary>
    ///     Indicates that the default palette should not be loaded.
    /// </summary>
    NoPalette = 0x0008,

    /// <summary>
    ///     Indicates that the video memory should not be cleared.
    /// </summary>
    NoClearMem = 0x8000
}