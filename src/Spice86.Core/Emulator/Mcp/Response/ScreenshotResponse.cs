namespace Spice86.Core.Emulator.Mcp.Response;

internal sealed record ScreenshotResponse {
    public required int Width { get; init; }

    public required int Height { get; init; }

    public required string Format { get; init; }

    public required string MimeType { get; init; }

    public required string FilePath { get; init; }

    public required string FileUri { get; init; }

    public required long FileSizeBytes { get; init; }
}
