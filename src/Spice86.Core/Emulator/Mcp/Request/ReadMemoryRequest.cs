namespace Spice86.Core.Emulator.Mcp.Request;

using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Request DTO for read_memory tool.
/// </summary>
public sealed record ReadMemoryRequest {
    /// <summary>
    /// Gets the starting memory address to read from.
    /// </summary>
    public required SegmentedAddress Address { get; init; }

    /// <summary>
    /// Gets the number of bytes to read.
    /// </summary>
    public required int Length { get; init; }
}
