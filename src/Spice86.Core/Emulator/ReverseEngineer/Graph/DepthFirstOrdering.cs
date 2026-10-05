namespace Spice86.Core.Emulator.ReverseEngineer.Graph;

using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Depth-first traversal from ordered roots, recording post-order, a tree-ordered reverse
/// post-order and the retreating edges. Successor order is the caller's.
/// </summary>
/// <typeparam name="T">The node type.</typeparam>
public sealed class DepthFirstOrdering<T> where T : notnull {
    private readonly List<T> _postOrder = [];
    private readonly List<GraphEdge<T>> _retreatingEdges = [];
    private readonly Dictionary<T, int> _postOrderIndexByNode = [];
    private readonly HashSet<T> _visited = [];
    private readonly HashSet<T> _onStack = [];
    private readonly List<int> _treeStartIndices = [];
    private readonly List<T> _reversePostOrder;

    /// <summary>
    /// Initializes a new instance of the <see cref="DepthFirstOrdering{T}"/> class.
    /// </summary>
    /// <param name="roots">Roots, in the order to start trees.</param>
    /// <param name="getSuccessors">Successors of a node, in the order to examine them.</param>
    public DepthFirstOrdering(IEnumerable<T> roots, Func<T, IEnumerable<T>> getSuccessors) {
        foreach (T root in roots) {
            if (!_visited.Contains(root)) {
                _treeStartIndices.Add(_postOrder.Count);
                Traverse(root, getSuccessors);
            }
        }

        List<T> result = [];
        for (int i = 0; i < _treeStartIndices.Count; i++) {
            int start = _treeStartIndices[i];
            int end = (i + 1 < _treeStartIndices.Count) ? _treeStartIndices[i + 1] : _postOrder.Count;
            for (int j = end - 1; j >= start; j--) {
                result.Add(_postOrder[j]);
            }
        }
        _reversePostOrder = result;
    }

    private void Traverse(T start, Func<T, IEnumerable<T>> getSuccessors) {
        List<Frame> stack = [];
        stack.Add(new Frame(start, getSuccessors(start).ToList(), 0));
        _visited.Add(start);
        _onStack.Add(start);

        while (stack.Count > 0) {
            Frame frame = stack[^1];
            if (frame.Cursor < frame.Successors.Count) {
                T successor = frame.Successors[frame.Cursor];
                frame.Cursor++;
                if (!_visited.Contains(successor)) {
                    _visited.Add(successor);
                    _onStack.Add(successor);
                    stack.Add(new Frame(successor, getSuccessors(successor).ToList(), 0));
                } else if (_onStack.Contains(successor)) {
                    _retreatingEdges.Add(new GraphEdge<T>(frame.Node, successor));
                }
            } else {
                stack.RemoveAt(stack.Count - 1);
                _onStack.Remove(frame.Node);
                _postOrderIndexByNode[frame.Node] = _postOrder.Count;
                _postOrder.Add(frame.Node);
            }
        }
    }

    /// <summary>
    /// Every visited node once, in the order it finished.
    /// </summary>
    public IReadOnlyList<T> PostOrder => _postOrder;

    /// <summary>
    /// The trees in root order (the order they were started), each tree's nodes in the reverse of that tree's own finishing order.
    /// </summary>
    public IReadOnlyList<T> ReversePostOrder => _reversePostOrder;

    /// <summary>
    /// For each visited node, its index in <see cref="PostOrder"/>.
    /// </summary>
    public IReadOnlyDictionary<T, int> PostOrderIndexByNode => _postOrderIndexByNode;

    /// <summary>
    /// Edges whose target was on the depth-first stack when the edge was examined. Every
    /// directed cycle reached by the traversal contributes at least one, whether the graph is reducible or not.
    /// Edges to finished nodes are not recorded.
    /// </summary>
    public IReadOnlyList<GraphEdge<T>> RetreatingEdges => _retreatingEdges;

    /// <summary>
    /// Returns true iff <paramref name="node"/> was visited.
    /// </summary>
    public bool Contains(T node) => _visited.Contains(node);

    private sealed class Frame {
        public Frame(T node, List<T> successors, int cursor) {
            Node = node;
            Successors = successors;
            Cursor = cursor;
        }

        public T Node { get; }
        public List<T> Successors { get; }
        public int Cursor { get; set; }
    }
}
