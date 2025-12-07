namespace Spice86.Core.Emulator.Mcp.Schema;

/// <summary>
/// Input schema properties for function list operation.
/// </summary>
internal sealed record FunctionListInputProperties {
    public required JsonSchemaProperty Limit { get; init; }
}
