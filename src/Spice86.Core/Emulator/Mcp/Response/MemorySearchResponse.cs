namespace Spice86.Core.Emulator.Mcp.Response;

internal sealed record MemorySearchResponse : McpToolResponse {
    public required string Pattern { get; init; }

    public required McpSegmentedAddress StartAddress { get; init; }

    public required int Length { get; init; }

    public required McpSegmentedAddress[] Matches { get; init; }

    public required bool Truncated { get; init; }
}
