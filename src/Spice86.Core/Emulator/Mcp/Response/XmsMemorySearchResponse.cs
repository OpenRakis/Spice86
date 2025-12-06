namespace Spice86.Core.Emulator.Mcp.Response;

internal sealed record XmsMemorySearchResponse : McpToolResponse {
    public required int Handle { get; init; }

    public required string Pattern { get; init; }

    public required uint StartOffset { get; init; }

    public required int Length { get; init; }

    public required uint[] Matches { get; init; }

    public required bool Truncated { get; init; }
}
