namespace Spice86.Core.Emulator.OperatingSystem.Enums;

/// <summary>Result of performing operations related to building DOS paths.</summary>
internal enum DosPathBuilderResult {
    /// <summary>Operation successful.</summary>
    Success = 0,

    /// <summary>Operation not successful due to path builder being frozen (immutable).</summary>
    PathBuilderFrozen,

    /// <summary>Operation not successful due to an invalid drive specification.</summary>
    InvalidDriveSpecification,

    /// <summary>Operation not successful due to path containing invalid file name characters.</summary>
    InvalidFileNameCharacters,

    /// <summary>Operation not successful due to path containing a reserved/invalid/device file name.</summary>
    InvalidReservedFileName
}
