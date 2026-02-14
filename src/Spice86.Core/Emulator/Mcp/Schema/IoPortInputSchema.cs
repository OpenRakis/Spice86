namespace Spice86.Core.Emulator.Mcp.Schema;

internal sealed record IoPortInputSchema {
    public required string Type { get; init; }
    public required IoPortInputProperties Properties { get; init; }
    public required string[] Required { get; init; }
}
