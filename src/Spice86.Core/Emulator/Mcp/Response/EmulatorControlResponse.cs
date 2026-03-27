namespace Spice86.Core.Emulator.Mcp.Response;

internal sealed record EmulatorControlResponse {
    public required bool Success { get; init; }

    public required string Message { get; init; }
}
