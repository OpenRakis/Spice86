namespace Spice86.Core.Emulator.Mcp.Response;

/// <summary>
/// Response for EMS memory read operation.
/// </summary>
public sealed record EmsMemoryReadResponse : McpToolResponse {
    /// <summary>
    /// Gets the EMS handle ID that was read.
    /// </summary>
    public required int Handle { get; init; }

    /// <summary>
    /// Gets the logical page number that was read.
    /// </summary>
    public required int LogicalPage { get; init; }

    /// <summary>
    /// Gets the offset within the logical page that was read.
    /// </summary>
    public required int Offset { get; init; }

    /// <summary>
    /// Gets the number of bytes that were read.
    /// </summary>
    public required int Length { get; init; }

    /// <summary>
    /// Gets the memory data as a hexadecimal string.
    /// </summary>
    public required string Data { get; init; }
}
