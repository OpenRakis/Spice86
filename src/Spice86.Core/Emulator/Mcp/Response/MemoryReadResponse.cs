namespace Spice86.Core.Emulator.Mcp.Response;

internal sealed record MemoryReadResponse : HexDataResponse {
    public required McpSegmentedAddress Address { get; init; }
}
