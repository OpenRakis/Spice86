namespace Spice86.ViewModels.DataModels;

using Spice86.ViewModels.Enums;

/// <summary>
/// Graph node used by DOS graph tabs.
/// </summary>
public sealed class DosGraphNode : IEquatable<DosGraphNode> {
    /// <summary>
    /// Stable node identifier used for equality in graph rendering.
    /// </summary>
    public int NodeId { get; init; }

    /// <summary>
    /// Primary line of text.
    /// </summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>
    /// Secondary line of text.
    /// </summary>
    public string Subtitle { get; init; } = string.Empty;

    /// <summary>
    /// Semantic kind for theme and rendering decisions.
    /// </summary>
    public DosGraphNodeKind Kind { get; init; } = DosGraphNodeKind.Psp;

    public bool Equals(DosGraphNode? other) {
        if (other is null) {
            return false;
        }
        return NodeId == other.NodeId;
    }

    public override bool Equals(object? obj) {
        return Equals(obj as DosGraphNode);
    }

    public override int GetHashCode() {
        return NodeId;
    }

    public override string ToString() {
        if (string.IsNullOrEmpty(Subtitle)) {
            return Title;
        }
        return Title + System.Environment.NewLine + Subtitle;
    }
}
