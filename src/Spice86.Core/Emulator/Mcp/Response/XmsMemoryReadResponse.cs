namespace Spice86.Core.Emulator.Mcp.Response;

internal sealed record XmsMemoryReadResponse : HexDataResponse {
    public required int Handle { get; init; }

    public required uint Offset { get; init; }
}
