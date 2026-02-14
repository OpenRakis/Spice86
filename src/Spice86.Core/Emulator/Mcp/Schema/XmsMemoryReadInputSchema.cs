namespace Spice86.Core.Emulator.Mcp.Schema;

/// <summary>
/// Input schema for XMS memory read operation.
/// </summary>
internal sealed record XmsMemoryReadInputSchema {
    public required string Type { get; init; }
    public required XmsMemoryReadInputProperties Properties { get; init; }
    public required string[] Required { get; init; }
}
