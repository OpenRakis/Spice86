namespace Spice86.Shared.Emulator.Storage.FileSystem.BootSector;

/// <summary>
/// Severity of a <see cref="BpbValidationIssue"/>.
/// </summary>
public enum BpbValidationSeverity {
    /// <summary>Informational note. Volume is usable.</summary>
    Info,
    /// <summary>Field value is unusual but volume is usable.</summary>
    Warning,
    /// <summary>Volume cannot be safely mounted as the indicated FAT type.</summary>
    Error
}
