namespace Spice86.Core.Emulator.Mcp.Schema;

/// <summary>
/// Input schema for memory read operation.
/// </summary>
internal sealed record MemoryReadInputSchema {
    public required string Type { get; init; }
    public required MemoryReadInputProperties Properties { get; init; }
    public required string[] Required { get; init; }
}
