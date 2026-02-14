namespace Spice86.Core.Emulator.Mcp.Response;

public sealed record ScreenshotResponse : McpToolResponse {
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required string Format { get; init; }
    public required string Data { get; init; }
}
