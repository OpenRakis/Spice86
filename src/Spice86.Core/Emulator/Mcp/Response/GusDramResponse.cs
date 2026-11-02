namespace Spice86.Core.Emulator.Mcp.Response;

internal sealed record GusDramResponse
{
    public required int Address { get; init; }

    public required int Length { get; init; }

    public required string Data { get; init; }
}
