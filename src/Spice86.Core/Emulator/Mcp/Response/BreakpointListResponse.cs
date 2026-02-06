namespace Spice86.Core.Emulator.Mcp.Response;

public sealed record BreakpointListResponse : McpToolResponse {
    public required List<BreakpointInfo> Breakpoints { get; init; }
}
