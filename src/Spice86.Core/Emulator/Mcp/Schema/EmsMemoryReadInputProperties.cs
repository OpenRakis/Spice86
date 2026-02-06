namespace Spice86.Core.Emulator.Mcp.Schema;

/// <summary>
/// Input schema properties for EMS memory read operation.
/// </summary>
internal sealed record EmsMemoryReadInputProperties {
    public required JsonSchemaProperty Handle { get; init; }
    public required JsonSchemaProperty LogicalPage { get; init; }
    public required JsonSchemaProperty Offset { get; init; }
    public required JsonSchemaProperty Length { get; init; }
}
