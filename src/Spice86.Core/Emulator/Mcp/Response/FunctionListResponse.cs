namespace Spice86.Core.Emulator.Mcp.Response;

/// <summary>
/// Response for function list query.
/// </summary>
public sealed record FunctionListResponse : McpToolResponse {
    /// <summary>
    /// Gets the array of functions.
    /// </summary>
    public required FunctionInfo[] Functions { get; init; }
}
