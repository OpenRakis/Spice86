namespace Spice86.ViewModels;

/// <summary>
/// One row of the DOS memory summary table (conventional, UMB, HMA, XMS, EMS, or system total).
/// </summary>
public sealed class MemoryReportRow {
    /// <summary>Memory category label (e.g. "Conventional").</summary>
    public string Category { get; init; } = string.Empty;

    /// <summary>Total bytes available for the category.</summary>
    public long TotalBytes { get; init; }

    /// <summary>Total kilobytes available for the category.</summary>
    public double TotalKilobytes => TotalBytes / 1024.0;

    /// <summary>Used bytes.</summary>
    public long UsedBytes { get; init; }

    /// <summary>Used kilobytes.</summary>
    public double UsedKilobytes => UsedBytes / 1024.0;

    /// <summary>Free bytes.</summary>
    public long FreeBytes { get; init; }

    /// <summary>Free kilobytes.</summary>
    public double FreeKilobytes => FreeBytes / 1024.0;

    /// <summary>Optional remark (driver status, HMA flag, etc.).</summary>
    public string Notes { get; init; } = string.Empty;
}
