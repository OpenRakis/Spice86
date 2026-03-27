namespace Spice86.Core.Emulator.Mcp.Response;

using Spice86.Core.Emulator.InterruptHandlers.VGA.Records;

internal sealed record VideoDetailedStateResponse {
    public required int BiosVideoMode { get; init; }

    public required VgaMode Mode { get; init; }

    public required CursorPosition Cursor { get; init; }

    public required int ScreenColumns { get; init; }

    public required int ScreenRows { get; init; }

    public required int RendererWidth { get; init; }

    public required int RendererHeight { get; init; }

    public required int BufferSize { get; init; }
}