namespace Spice86.Core.Emulator.Mcp.Response;

internal sealed record VideoStateResponse {
    public required int Width { get; init; }

    public required int Height { get; init; }

    public required int BufferSize { get; init; }
}
