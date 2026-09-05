namespace Spice86.Core.Emulator.Mcp.Response;

using System.Collections.Generic;

internal sealed record GusDramSearchResponse
{
    public required string Pattern { get; init; }

    public required int StartAddress { get; init; }

    public required int Length { get; init; }

    public required IReadOnlyList<int> Matches { get; init; }

    public required bool Truncated { get; init; }
}
