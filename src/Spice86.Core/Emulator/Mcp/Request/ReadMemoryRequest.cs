namespace Spice86.Core.Emulator.Mcp.Request;

/// <summary>
/// Request DTO for read_memory tool.
/// </summary>
public sealed record ReadMemoryRequest {
    /// <summary>
    /// Gets the starting memory address to read from.
    /// </summary>
    public required uint Address { get; init; }

    /// <summary>
    /// Gets the number of bytes to read.
    /// </summary>
    public required int Length { get; init; }
}
