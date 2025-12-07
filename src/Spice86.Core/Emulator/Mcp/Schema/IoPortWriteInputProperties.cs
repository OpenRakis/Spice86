namespace Spice86.Core.Emulator.Mcp.Schema;

internal sealed record IoPortWriteInputProperties {
    public required JsonSchemaProperty Port { get; init; }
    public required JsonSchemaProperty Value { get; init; }
}
