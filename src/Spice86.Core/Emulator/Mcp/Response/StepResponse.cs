namespace Spice86.Core.Emulator.Mcp.Response;

internal sealed record StepResponse {
    public required bool Success { get; init; }

    public required string Message { get; init; }

    public required CpuStateSnapshot CpuState { get; init; }
}
