namespace Spice86.Core.Emulator.Mcp.Response;

internal abstract record PatternSearchResponse<TMatch> {
    public required string Pattern { get; init; }

    public required int Length { get; init; }

    public required TMatch[] Matches { get; init; }

    public required bool Truncated { get; init; }
}