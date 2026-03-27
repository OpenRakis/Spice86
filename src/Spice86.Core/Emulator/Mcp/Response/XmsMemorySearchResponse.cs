namespace Spice86.Core.Emulator.Mcp.Response;

internal sealed record XmsMemorySearchResponse : PatternSearchResponse<uint> {
    public required int Handle { get; init; }

    public required uint StartOffset { get; init; }
}
