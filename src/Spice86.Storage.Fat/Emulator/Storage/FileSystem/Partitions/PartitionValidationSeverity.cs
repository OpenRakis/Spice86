namespace Spice86.Shared.Emulator.Storage.FileSystem.Partitions;

/// <summary>
/// Validation severity for partition diagnostics.
/// </summary>
public enum PartitionValidationSeverity
{
    /// <summary>Informational diagnostic.</summary>
    Info,

    /// <summary>Potential issue that might still be readable.</summary>
    Warning,

    /// <summary>Invalid state that should be treated as an error.</summary>
    Error
}
