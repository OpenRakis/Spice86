namespace Spice86.Core.Emulator.Mcp.Response;

internal sealed record EmsPageFrameReadResponse : HexDataResponse {
    public required int PhysicalPage { get; init; }

    public required int Offset { get; init; }
}
