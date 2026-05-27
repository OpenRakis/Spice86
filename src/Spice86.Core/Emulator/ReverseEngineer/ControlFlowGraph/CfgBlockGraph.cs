namespace Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph;

/// <summary>
/// Pure traversal result shared by both JSON and UI consumers. Contains no execution-context metadata.
/// </summary>
public sealed record CfgBlockGraph {
    public required CfgBlockGraphNode[] Blocks { get; init; }
    public required CfgBlockGraphEdge[] Edges { get; init; }
    public required bool Truncated { get; init; }
}
