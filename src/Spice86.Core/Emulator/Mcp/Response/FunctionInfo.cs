namespace Spice86.Core.Emulator.Mcp.Response;

/// <summary>
/// Information about a single function.
/// </summary>
public sealed record FunctionInfo {
    /// <summary>
    /// Gets the function address as a string.
    /// </summary>
    public required string Address { get; init; }

    /// <summary>
    /// Gets the function name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the number of times this function was called.
    /// </summary>
    public required int CalledCount { get; init; }

    /// <summary>
    /// Gets whether this function has a C# override.
    /// </summary>
    public required bool HasOverride { get; init; }
}
