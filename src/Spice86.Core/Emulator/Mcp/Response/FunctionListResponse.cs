namespace Spice86.Core.Emulator.Mcp.Response;

internal sealed record FunctionListResponse : McpToolResponse {
    public required FunctionInfo[] Functions { get; init; }

    public required int TotalCount { get; init; }
}
