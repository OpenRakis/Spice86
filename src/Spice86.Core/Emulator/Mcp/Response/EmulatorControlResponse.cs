namespace Spice86.Core.Emulator.Mcp.Response;

internal sealed record EmulatorControlResponse : McpToolResponse {
    public required bool Success { get; init; }

    public required string Message { get; init; }
}
