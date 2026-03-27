namespace Spice86.Core.Emulator.Mcp.Response;

internal abstract record HexDataResponse {
    public required int Length { get; init; }

    public required string Data { get; init; }
}