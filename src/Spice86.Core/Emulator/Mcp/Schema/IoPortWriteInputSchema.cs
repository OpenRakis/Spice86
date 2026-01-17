namespace Spice86.Core.Emulator.Mcp.Schema;

internal sealed record IoPortWriteInputSchema {
    public required string Type { get; init; }
    public required IoPortWriteInputProperties Properties { get; init; }
    public required string[] Required { get; init; }
}
