namespace Spice86.Core.Emulator.Mcp.Response;

internal sealed record EmsMemoryReadResponse : McpToolResponse {
    public required int Handle { get; init; }

    public required int LogicalPage { get; init; }

    public required int Offset { get; init; }

    public required int Length { get; init; }

    public required string Data { get; init; }
}
