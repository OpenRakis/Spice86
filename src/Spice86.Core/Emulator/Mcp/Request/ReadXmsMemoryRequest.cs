namespace Spice86.Core.Emulator.Mcp.Request;

/// <summary>
/// Request DTO for read_xms_memory tool.
/// </summary>
public sealed record ReadXmsMemoryRequest {
    /// <summary>
    /// Gets the XMS handle ID to read from.
    /// </summary>
    public required int Handle { get; init; }

    /// <summary>
    /// Gets the offset within the XMS block.
    /// </summary>
    public required uint Offset { get; init; }

    /// <summary>
    /// Gets the number of bytes to read.
    /// </summary>
    public required int Length { get; init; }
}
