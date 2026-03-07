namespace Spice86.Core.Emulator.Mcp.Response;

using Spice86.Core.Emulator.InterruptHandlers.VGA.Records;

public sealed record VideoStateResponse : McpToolResponse {
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required int BufferSize { get; init; }
    public required VgaMode Mode { get; init; }
}
