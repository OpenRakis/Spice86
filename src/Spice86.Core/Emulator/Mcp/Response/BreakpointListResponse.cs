namespace Spice86.Core.Emulator.Mcp.Response;

internal sealed record BreakpointListResponse {
    public required List<BreakpointInfo> Breakpoints { get; init; }
}
