namespace Spice86.Core.Emulator.StateSerialization.ControlFlow;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;

/// <summary>
/// Represents one cross-block edge in the exported CFG graph. <see cref="BridgeNode"/> is the
/// specific entry already resolved from the source block terminator's successor list during BFS
/// traversal. UI adapters use it to derive the edge label and color without re-scanning the terminator.
/// </summary>
public sealed record CfgBlockGraphEdge {
    public required CfgBlock From { get; init; }
    public required CfgBlock To { get; init; }
    public required ICfgNode BridgeNode { get; init; }
}
