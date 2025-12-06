namespace Spice86.Core.Emulator.Mcp.Response;

internal sealed record VideoPixelResponse : McpToolResponse {
    public required int X { get; init; }

    public required int Y { get; init; }

    public required int Color { get; init; }
}