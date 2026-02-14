namespace Spice86.Core.Emulator.Mcp.Response;

/// <summary>
/// Response for memory read operation.
/// </summary>
public sealed record MemoryReadResponse : McpToolResponse {
    /// <summary>
    /// Gets the starting address that was read.
    /// </summary>
    public required uint Address { get; init; }

    /// <summary>
    /// Gets the number of bytes that were read.
    /// </summary>
    public required int Length { get; init; }

    /// <summary>
    /// Gets the memory data as a hexadecimal string.
    /// </summary>
    public required string Data { get; init; }
}
