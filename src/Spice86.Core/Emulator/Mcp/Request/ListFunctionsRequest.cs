namespace Spice86.Core.Emulator.Mcp.Request;

/// <summary>
/// Request DTO for list_functions tool.
/// </summary>
public sealed record ListFunctionsRequest {
    /// <summary>
    /// Gets the optional limit on number of functions to return.
    /// If not specified, returns all functions.
    /// </summary>
    public int? Limit { get; init; }
}
