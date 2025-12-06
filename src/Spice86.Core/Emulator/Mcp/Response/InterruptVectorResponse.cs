namespace Spice86.Core.Emulator.Mcp.Response;

internal sealed record InterruptVectorResponse : McpToolResponse {
    public required int VectorNumber { get; init; }

    public required McpSegmentedAddress Address { get; init; }
}