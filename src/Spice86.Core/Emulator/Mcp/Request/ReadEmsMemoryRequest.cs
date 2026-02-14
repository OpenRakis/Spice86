namespace Spice86.Core.Emulator.Mcp.Request;

/// <summary>
/// Request DTO for read_ems_memory tool.
/// </summary>
public sealed record ReadEmsMemoryRequest {
    /// <summary>
    /// Gets the EMS handle ID to read from.
    /// </summary>
    public required int Handle { get; init; }

    /// <summary>
    /// Gets the logical page number within the handle.
    /// </summary>
    public required int LogicalPage { get; init; }

    /// <summary>
    /// Gets the offset within the logical page.
    /// </summary>
    public required int Offset { get; init; }

    /// <summary>
    /// Gets the number of bytes to read.
    /// </summary>
    public required int Length { get; init; }
}
