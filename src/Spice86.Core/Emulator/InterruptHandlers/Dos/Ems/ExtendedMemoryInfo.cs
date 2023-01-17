namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;

/// <summary>
/// Contains information about extended memory usage.
/// </summary>
public sealed class ExtendedMemoryInfo {
    internal ExtendedMemoryInfo(int bytesAllocated, int totalBytes) {
        this.BytesAllocated = bytesAllocated;
        this.TotalBytes = totalBytes;
    }

    /// <summary>
    /// Gets the number of XMS bytes currently allocated.
    /// </summary>
    public int BytesAllocated { get; }

    /// <summary>
    /// Gets the number of XMS bytes currently free.
    /// </summary>
    public int BytesFree => this.TotalBytes - this.BytesAllocated;

    /// <summary>
    /// Gets the total number of XMS bytes.
    /// </summary>
    public int TotalBytes { get; }
}
