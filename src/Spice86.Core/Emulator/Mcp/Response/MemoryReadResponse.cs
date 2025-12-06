namespace Spice86.Core.Emulator.Mcp.Response;

internal sealed record MemoryReadResponse : McpToolResponse {
    public required McpSegmentedAddress Address { get; init; }

    public required int Length { get; init; }

    public required string Data { get; init; }
}
