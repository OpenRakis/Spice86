namespace Spice86.Core.Emulator.Memory;

/// <summary>
/// Contains information about expanded memory usage.
/// </summary>
public sealed class ExpandedMemoryInfo {
    /// <summary>
    /// Size of expanded memory in pages.
    /// </summary>
    public const int TotalPages = ExpandedMemoryManager.MaximumLogicalPages;

    /// <summary>
    /// Size of expanded memory in bytes.
    /// </summary>
    public const int TotalBytes = TotalPages * ExpandedMemoryManager.PageSize;

    internal ExpandedMemoryInfo(int allocatedPages) => this.PagesAllocated = allocatedPages;

    /// <summary>
    /// Gets the number of pages allocated.
    /// </summary>
    public int PagesAllocated { get; }

    /// <summary>
    /// Gets the number of bytes allocated.
    /// </summary>
    public int BytesAllocated => this.PagesAllocated * ExpandedMemoryManager.PageSize;

    /// <summary>
    /// Gets the number of free pages.
    /// </summary>
    public int PagesFree => Math.Max(TotalPages - this.PagesAllocated, 0);

    /// <summary>
    /// Gets the number of free bytes.
    /// </summary>
    public int BytesFree => Math.Max(TotalBytes - this.BytesAllocated, 0);
}
