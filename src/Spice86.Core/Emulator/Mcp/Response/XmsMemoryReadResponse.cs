namespace Spice86.Core.Emulator.Mcp.Response;

/// <summary>
/// Response for XMS memory read operation.
/// </summary>
public sealed record XmsMemoryReadResponse : McpToolResponse {
    /// <summary>
    /// Gets the XMS handle ID that was read.
    /// </summary>
    public required int Handle { get; init; }

    /// <summary>
    /// Gets the offset within the XMS block that was read.
    /// </summary>
    public required uint Offset { get; init; }

    /// <summary>
    /// Gets the number of bytes that were read.
    /// </summary>
    public required int Length { get; init; }

    /// <summary>
    /// Gets the memory data as a hexadecimal string.
    /// </summary>
    public required string Data { get; init; }
}
