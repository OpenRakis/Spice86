namespace Spice86.Core.Emulator.Mcp.Response;

public sealed record BreakpointInfo : McpToolResponse {
    public required string Id { get; init; }
    public required long Address { get; init; }
    public required string Type { get; init; }
    public string? Condition { get; init; }
    public required bool IsEnabled { get; init; }
}
