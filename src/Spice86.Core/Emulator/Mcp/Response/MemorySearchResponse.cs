namespace Spice86.Core.Emulator.Mcp.Response;

internal sealed record MemorySearchResponse : PatternSearchResponse<McpSegmentedAddress> {

    public required McpSegmentedAddress StartAddress { get; init; }
}
