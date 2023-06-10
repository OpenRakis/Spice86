namespace Spice86.Core.Emulator.Devices.Video.Registers.Enums;

/// <summary>
///     Names of the graphics controller registers.
/// </summary>
public enum GraphicsControllerRegister {
    /// <summary>
    ///     Set/Reset Register
    /// </summary>
    SetReset,

    /// <summary>
    ///     Enable Set/Reset Register
    /// </summary>
    EnableSetReset,

    /// <summary>
    ///     Color Compare Register
    /// </summary>
    ColorCompare,

    /// <summary>
    ///     Data Rotate Register
    /// </summary>
    DataRotate,

    /// <summary>
    ///     Read Map Select Register
    /// </summary>
    ReadMapSelect,

    /// <summary>
    ///     Graphics Mode Register
    /// </summary>
    GraphicsMode,

    /// <summary>
    ///     Miscellaneous Register
    /// </summary>
    MiscellaneousGraphics,

    /// <summary>
    ///     Color Don't Care Register
    /// </summary>
    ColorDontCare,

    /// <summary>
    ///     Bit Mask Register
    /// </summary>
    BitMask
}