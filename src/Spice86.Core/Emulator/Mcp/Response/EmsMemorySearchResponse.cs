namespace Spice86.Core.Emulator.Mcp.Response;

internal sealed record EmsMemorySearchResponse : PatternSearchResponse<int> {
    public required int Handle { get; init; }

    public required int LogicalPage { get; init; }

    public required int StartOffset { get; init; }
}
