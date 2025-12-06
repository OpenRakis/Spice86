namespace Spice86.Core.Emulator.Mcp.Response;

internal sealed record McpSegmentedAddress {
    public required ushort Segment { get; init; }

    public required ushort Offset { get; init; }
}
