namespace Spice86.Core.Emulator.Mcp.Response;

using Spice86.Shared.Emulator.Memory;

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
    /// Gets the memory data as a byte array.
    /// </summary>
    public required byte[] Data { get; init; }
}
