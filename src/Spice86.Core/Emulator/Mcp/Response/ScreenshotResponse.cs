namespace Spice86.Core.Emulator.Mcp.Response;

public sealed record ScreenshotResponse : McpToolResponse {
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required PixelFormat Format { get; init; } = PixelFormat.Bgra32;
    public required byte[] Data { get; init; }
}

public enum PixelFormat {
    Rgb24,
    Bgr24,
    Rgba32,
    Bgra32
}