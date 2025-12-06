namespace Spice86.Core.Emulator.Mcp.Response;

internal sealed record BreakpointListResponse : McpToolResponse {
    public required List<BreakpointInfo> Breakpoints { get; init; }
}
