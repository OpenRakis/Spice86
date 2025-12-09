namespace Spice86.Core.Emulator.Mcp.Response;

/// <summary>
/// Response for CFG CPU graph inspection.
/// </summary>
public sealed record CfgCpuGraphResponse : McpToolResponse {
    /// <summary>
    /// Gets the current execution context depth.
    /// Depth 0 is the initial context, higher values indicate nested interrupt contexts.
    /// </summary>
    public required int CurrentContextDepth { get; init; }

    /// <summary>
    /// Gets the entry point address of the current execution context.
    /// </summary>
    public required string CurrentContextEntryPoint { get; init; }

    /// <summary>
    /// Gets the total number of entry points across all contexts.
    /// </summary>
    public required int TotalEntryPoints { get; init; }

    /// <summary>
    /// Gets the addresses of all entry points in the CFG graph.
    /// </summary>
    public required string[] EntryPointAddresses { get; init; }

    /// <summary>
    /// Gets the address of the last executed instruction.
    /// </summary>
    public required string LastExecutedAddress { get; init; }
}
