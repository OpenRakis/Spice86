namespace Spice86.Core.Emulator.OperatingSystem.Enums;

/// <summary>
/// Return codes for FCB filename parsing operations.
/// </summary>
public enum FcbParseResult : byte {
    /// <summary>
    /// No wildcards found in the filename.
    /// </summary>
    NoWildcards = 0,
    
    /// <summary>
    /// Wildcards found in the filename.
    /// </summary>
    Wildcards = 1,
    
    /// <summary>
    /// Invalid drive specified.
    /// </summary>
    InvalidDrive = 0xFF
}