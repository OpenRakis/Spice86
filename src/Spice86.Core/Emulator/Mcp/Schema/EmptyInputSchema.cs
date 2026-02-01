namespace Spice86.Core.Emulator.Mcp.Schema;

/// <summary>
/// Empty input schema for tools with no parameters.
/// </summary>
internal sealed record EmptyInputSchema {
    public required string Type { get; init; }
    public required EmptySchemaProperties Properties { get; init; }
    public required string[] Required { get; init; }
}
