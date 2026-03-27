namespace Spice86.Core.Emulator.Mcp.Response;

internal sealed record VideoDetailedStateResponse {
    public required int BiosVideoMode { get; init; }

    public required string MemoryModel { get; init; }

    public required int Width { get; init; }

    public required int Height { get; init; }

    public required int BitsPerPixel { get; init; }

    public required int CharacterWidth { get; init; }

    public required int CharacterHeight { get; init; }

    public required int StartSegment { get; init; }

    public required int ScreenColumns { get; init; }

    public required int ScreenRows { get; init; }

    public required int ActivePage { get; init; }

    public required int CursorX { get; init; }

    public required int CursorY { get; init; }

    public required int RendererWidth { get; init; }

    public required int RendererHeight { get; init; }

    public required int BufferSize { get; init; }
}