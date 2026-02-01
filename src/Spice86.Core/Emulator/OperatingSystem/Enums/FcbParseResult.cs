namespace Spice86.Core.Emulator.OperatingSystem.Enums;

/// <summary>
/// Return codes for INT 21h AH=29h filename parsing into an FCB, matching FreeDOS behavior.
/// </summary>
public enum FcbParseResult : byte {
    /// <summary>
    /// Parsed successfully with no wildcards encountered.
    /// </summary>
    NoWildcards = 0x00,

    /// <summary>
    /// Parsed successfully and wildcards were present in the input name or extension.
    /// </summary>
    WildcardsPresent = 0x01,

    /// <summary>
    /// Invalid drive letter was supplied.
    /// </summary>
    InvalidDrive = 0xFF
}
