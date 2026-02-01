namespace Spice86.Core.Emulator.Mcp.Schema;

/// <summary>
/// Input schema for EMS memory read operation.
/// </summary>
internal sealed record EmsMemoryReadInputSchema {
    public required string Type { get; init; }
    public required EmsMemoryReadInputProperties Properties { get; init; }
    public required string[] Required { get; init; }
}
