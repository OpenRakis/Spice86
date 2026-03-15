namespace Spice86.Core.Emulator.Mcp.Response;

/// <summary>
/// Response to a memory search request.
/// </summary>
public sealed record MemorySearchResponse : McpToolResponse {
    /// <summary>
    /// Byte pattern that was searched for, represented as a hex string.
    /// </summary>
    public required string Pattern { get; init; }

    /// <summary>
    /// Start address of the search.
    /// </summary>
    public required uint StartAddress { get; init; }

    /// <summary>
    /// Length of the search.
    /// </summary>
    public required int Length { get; init; }

    /// <summary>
    /// List of addresses where the pattern was found.
    /// </summary>
    public required uint[] Matches { get; init; }

    /// <summary>
    /// Whether the search was truncated.
    /// </summary>
    public required bool Truncated { get; init; }
}