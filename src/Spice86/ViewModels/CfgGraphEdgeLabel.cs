namespace Spice86.ViewModels;

/// <summary>
/// View model representing an edge label in the CFG graph, carrying display text
/// and an edge type for automatic connection coloring.
/// </summary>
public sealed class CfgGraphEdgeLabel {
    /// <summary>
    /// Display text for the edge label.
    /// </summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>
    /// The type of edge, used to pick a theme-aware color for the connection line.
    /// </summary>
    public CfgEdgeType EdgeType { get; init; }

    public override string ToString() => Text;
}

/// <summary>
/// Classifies CFG graph edges for automatic color assignment.
/// </summary>
public enum CfgEdgeType {
    Normal,
    Jump,
    Call,
    Return,
    Selector,
    CallToReturn,
    CpuFault
}
