namespace Spice86.Core.Emulator.Mcp.Schema;

/// <summary>
/// Input schema properties for XMS memory read operation.
/// </summary>
internal sealed record XmsMemoryReadInputProperties {
    public required JsonSchemaProperty Handle { get; init; }
    public required JsonSchemaProperty Offset { get; init; }
    public required JsonSchemaProperty Length { get; init; }
}
