namespace Spice86.Core.Emulator.Mcp.Response;

using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Information about a single function.
/// </summary>
public sealed record FunctionInfo {
    /// <summary>
    /// Gets the function address.
    /// </summary>
    public required SegmentedAddress Address { get; init; }

    /// <summary>
    /// Gets the function name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets whether this function has a C# override.
    /// </summary>
    public required bool HasOverride { get; init; }

    /// <summary>
    /// Gets the number of times this function has been called.
    /// </summary>
    public required int CalledCount { get; init; }
}
