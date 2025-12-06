namespace Spice86.Core.Emulator.Mcp.Response;

internal sealed record XmsMemoryReadResponse : McpToolResponse {
    public required int Handle { get; init; }

    public required uint Offset { get; init; }

    public required int Length { get; init; }

    public required string Data { get; init; }
}
