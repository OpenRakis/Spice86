namespace Spice86.Core.Emulator.Mcp.Response;

internal sealed record EmsPageFrameReadResponse : McpToolResponse {
    public required int PhysicalPage { get; init; }

    public required int Offset { get; init; }

    public required int Length { get; init; }

    public required string Data { get; init; }
}
