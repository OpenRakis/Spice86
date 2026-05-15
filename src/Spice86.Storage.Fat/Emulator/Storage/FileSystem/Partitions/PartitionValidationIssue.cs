namespace Spice86.Shared.Emulator.Storage.FileSystem.Partitions;

/// <summary>
/// One partition validation diagnostic.
/// </summary>
public sealed class PartitionValidationIssue
{
    /// <summary>Diagnostic severity.</summary>
    public PartitionValidationSeverity Severity { get; }

    /// <summary>Diagnostic message.</summary>
    public string Message { get; }

    /// <summary>
    /// Creates a validation issue.
    /// </summary>
    /// <param name="severity">Issue severity.</param>
    /// <param name="message">Issue message.</param>
    public PartitionValidationIssue(PartitionValidationSeverity severity, string message)
    {
        Severity = severity;
        Message = message;
    }
}
