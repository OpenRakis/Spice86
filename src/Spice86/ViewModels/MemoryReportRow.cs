namespace Spice86.ViewModels;

/// <summary>
/// One row of the DOS memory summary table (Conventional / HMA / XMS / EMS / total).
/// </summary>
public sealed class MemoryReportRow {
    /// <summary>Memory category label (e.g. "Conventional").</summary>
    public string Category { get; init; } = string.Empty;

    /// <summary>Total bytes available for the category.</summary>
    public long TotalBytes { get; init; }

    /// <summary>Used bytes.</summary>
    public long UsedBytes { get; init; }

    /// <summary>Free bytes.</summary>
    public long FreeBytes { get; init; }

    /// <summary>Optional remark (driver status, HMA flag, etc.).</summary>
    public string Notes { get; init; } = string.Empty;
}
