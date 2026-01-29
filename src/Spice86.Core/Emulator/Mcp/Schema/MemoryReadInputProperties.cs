namespace Spice86.Core.Emulator.Mcp.Schema;

/// <summary>
/// Input schema properties for memory read operation.
/// </summary>
internal sealed record MemoryReadInputProperties {
    public required JsonSchemaProperty Address { get; init; }
    public required JsonSchemaProperty Length { get; init; }
}
