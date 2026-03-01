namespace Spice86.Core.Emulator.Mcp.Response;

public sealed record VideoStateResponse : McpToolResponse {
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required int BufferSize { get; init; }
}
