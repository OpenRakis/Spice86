namespace Spice86.Core.Emulator.Mcp.Schema;

/// <summary>
/// Input schema for function list operation.
/// </summary>
internal sealed record FunctionListInputSchema {
    public required string Type { get; init; }
    public required FunctionListInputProperties Properties { get; init; }
    public required string[] Required { get; init; }
}
