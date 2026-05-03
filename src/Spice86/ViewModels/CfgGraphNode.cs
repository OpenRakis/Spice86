namespace Spice86.ViewModels;

using Spice86.DebuggerKnowledgeBase.Decoding;
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
    public bool IsLastExecuted { get; init; }

    /// <summary>
    /// The type of instruction this node represents, used for visual differentiation.
    /// </summary>
    public CfgNodeType NodeType { get; init; }

    /// <summary>
    /// Decoded high-level call information when this node represents a decodable instruction
    /// (INT, emulator-provided routine entry). Null for plain instructions.
    /// </summary>
    public DecodedCall? DecodedCall { get; init; }

    /// <summary>
    /// True when this node's address is the entry point of an emulator-provided function
    /// (name starts with <c>provided_</c>).
    /// </summary>
    public bool IsEmulatorProvided { get; init; }

    /// <summary>
    /// Function name when <see cref="IsEmulatorProvided"/> is true; null otherwise.
    /// </summary>
    public string? EmulatorProvidedFunctionName { get; init; }

    public override bool Equals(object? obj) => obj is CfgGraphNode other && NodeId == other.NodeId;
    public bool Equals(CfgGraphNode? other) => other is not null && NodeId == other.NodeId;
    public override int GetHashCode() => NodeId;
    public override string ToString() => string.Join("", TextOffsets.ConvertAll(s => s.Text));
}
