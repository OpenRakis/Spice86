namespace Spice86.Core.Emulator.Mcp.Response;

internal sealed record CpuStatusResponse : CpuRegistersResponse {
    public required long Cycles { get; init; }
}
