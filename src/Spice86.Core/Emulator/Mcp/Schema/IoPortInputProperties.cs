namespace Spice86.Core.Emulator.Mcp.Schema;

internal sealed record IoPortInputProperties {
    public required JsonSchemaProperty Port { get; init; }
}
