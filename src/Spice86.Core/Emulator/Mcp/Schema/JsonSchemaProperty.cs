namespace Spice86.Core.Emulator.Mcp.Schema;

/// <summary>
/// JSON schema property descriptor.
/// </summary>
internal sealed record JsonSchemaProperty {
    public required string Type { get; init; }
    public required string Description { get; init; }
}
