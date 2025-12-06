namespace Spice86.Core.Emulator.Mcp.Response;

internal sealed record EmsMemorySearchResponse : McpToolResponse
{
    public required int Handle { get; init; }

    public required int LogicalPage { get; init; }

    public required string Pattern { get; init; }

    public required int StartOffset { get; init; }

    public required int Length { get; init; }

    public required int[] Matches { get; init; }

    public required bool Truncated { get; init; }
}
