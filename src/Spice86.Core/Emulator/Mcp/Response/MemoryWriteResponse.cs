namespace Spice86.Core.Emulator.Mcp.Response;

internal sealed record MemoryWriteResponse : McpToolResponse
{
    public required McpSegmentedAddress Address { get; init; }

    public required int Length { get; init; }

    public required bool Success { get; init; }
}
