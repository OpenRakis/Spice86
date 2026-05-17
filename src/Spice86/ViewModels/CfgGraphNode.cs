namespace Spice86.ViewModels;

using Spice86.ViewModels.TextPresentation;

/// <summary>
/// View model representing a node in the CFG graph for rendering with syntax highlighting.
/// Used as the graph node object in AvaloniaGraphControl, with identity based on <see cref="NodeId"/>.
/// </summary>
public sealed class CfgGraphNode : IEquatable<CfgGraphNode> {
    /// <summary>
    /// Unique identifier matching the underlying <see cref="Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph.ICfgNode.Id"/>.
    /// </summary>
    public int NodeId { get; init; }

    /// <summary>
    /// Syntax-highlighted text offsets for rendering the full node text (header + assembly).
    /// </summary>
    public List<FormattedTextToken> TextOffsets { get; init; } = [];

    /// <summary>
    /// Whether this node is the last executed instruction.
    /// </summary>
    public bool IsExecuting { get; init; }

    /// <summary>
    /// Whether the underlying CfgBlock (or instruction) is currently live.
    /// Defaults to <c>true</c>. When <c>false</c>, the view applies "stale" styling
    /// to visually distinguish the node from a live block.
    /// </summary>
    public bool IsLive { get; init; } = true;

    /// <summary>
    /// Whether the underlying CfgBlock has finished discovery. Defaults to <c>true</c>
    /// for plain instruction nodes; for CfgBlock nodes this mirrors
    /// <see cref="Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph.CfgBlock.IsDiscoveryComplete"/>.
    /// When <c>false</c>, the view applies an in-progress indicator (e.g. trailing "…"
    /// on the listing and a dashed outline) to distinguish the node from a closed block.
    /// </summary>
    public bool IsDiscoveryComplete { get; init; } = true;

    public override bool Equals(object? obj) => obj is CfgGraphNode other && NodeId == other.NodeId;
    public bool Equals(CfgGraphNode? other) => other is not null && NodeId == other.NodeId;
    public override int GetHashCode() => NodeId;
    public override string ToString() => string.Join("", TextOffsets.ConvertAll(s => s.Text));
}