namespace Spice86.Core.Emulator.Mcp.Response;

public sealed record IoPortWriteResponse : McpToolResponse {
    public required int Port { get; init; }
    public required int Value { get; init; }
    public required bool Success { get; init; }
}
