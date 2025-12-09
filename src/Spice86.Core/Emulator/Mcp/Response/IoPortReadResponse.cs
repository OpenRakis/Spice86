namespace Spice86.Core.Emulator.Mcp.Response;

public sealed record IoPortReadResponse : McpToolResponse {
    public required int Port { get; init; }
    public required byte Value { get; init; }
}
