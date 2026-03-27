namespace Spice86.Core.Emulator.Mcp.Response;

internal sealed record EmsMemoryReadResponse : HexDataResponse {
    public required int Handle { get; init; }

    public required int LogicalPage { get; init; }

    public required int Offset { get; init; }
}
