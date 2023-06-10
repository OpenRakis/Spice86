namespace Spice86.Core.Emulator.Devices.Video.Registers.Enums;

/// <summary>
///     Names of the CRT controller registers.
/// </summary>
public enum CrtControllerRegister {
    /// <summary>
    ///     Horizontal Total Register
    /// </summary>
    HorizontalTotal,

    /// <summary>
    ///     Horizontal Display-Enable End Register
    /// </summary>
    HorizontalDisplayEnd,

    /// <summary>
    ///     Horizontal Blanking Start Register
    /// </summary>
    HorizontalBlankingStart,

    /// <summary>
    ///     Horizontal Blanking End Register
    /// </summary>
    HorizontalBlankingEnd,

    /// <summary>
    ///     Horizontal Retrace Start Register
    /// </summary>
    HorizontalRetraceStart,

    /// <summary>
    ///     Horizontal Retrace End Register
    /// </summary>
    HorizontalRetraceEnd,

    /// <summary>
    ///     Vertical Total Register
    /// </summary>
    VerticalTotal,

    /// <summary>
    ///     Overflow Register
    /// </summary>
    Overflow,

    /// <summary>
    ///     Preset Row Scan Register
    /// </summary>
    PresetRowScan,

    /// <summary>
    ///     Maximum Scan Line Register
    /// </summary>
    CharacterCellHeight,

    /// <summary>
    ///     Cursor Start Register
    /// </summary>
    CursorStart,

    /// <summary>
    ///     Cursor End Register
    /// </summary>
    CursorEnd,

    /// <summary>
    ///     Start Address High Register
    /// </summary>
    StartAddressHigh,

    /// <summary>
    ///     Start Address Low Register
    /// </summary>
    StartAddressLow,

    /// <summary>
    ///     Cursor Location High Register
    /// </summary>
    CursorLocationHigh,

    /// <summary>
    ///     Cursor Location Low Register
    /// </summary>
    CursorLocationLow,

    /// <summary>
    ///     Vertical Retrace Start Register
    /// </summary>
    VerticalRetraceStart,

    /// <summary>
    ///     Vertical Retrace End Register
    /// </summary>
    VerticalRetraceEnd,

    /// <summary>
    ///     Vertical Display Enable End Register
    /// </summary>
    VerticalDisplayEnd,

    /// <summary>
    ///     Offset Register
    /// </summary>
    Offset,

    /// <summary>
    ///     Underline Location Register
    /// </summary>
    UnderlineLocation,

    /// <summary>
    ///     Vertical Blanking Start Register
    /// </summary>
    VerticalBlankingStart,

    /// <summary>
    ///     Vertical Blanking End Register
    /// </summary>
    VerticalBlankingEnd,

    /// <summary>
    ///     Mode Control Register
    /// </summary>
    CrtModeControl,

    /// <summary>
    ///     Line Compare Register
    /// </summary>
    LineCompare
}