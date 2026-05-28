namespace Spice86.Core.Emulator.ReverseEngineer.Graph;

using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Strongly connected component utilities for directed graphs.
/// </summary>
public static class StronglyConnectedComponents {
    /// <summary>
    /// Finds strongly connected components with Tarjan's algorithm.
    /// </summary>
    /// <remarks>
    /// This implementation keeps its own depth-first search stack instead of using recursion, so it can process large
    /// generated graphs without depending on the process call-stack limit.
    /// </remarks>
    public static Dictionary<T, int> Find<T>(IEnumerable<T> nodes, Func<T, IEnumerable<T>> getNeighbors) where T : notnull {
        Dictionary<T, int> indexByNode = new();
        Dictionary<T, int> lowLinkByNode = new();
        Stack<T> componentStack = new();
        HashSet<T> onComponentStack = [];
        Stack<SearchFrame<T>> searchStack = new();
        Dictionary<T, int> componentByNode = new();
        int nextIndex = 0;
        int nextComponent = 0;

        foreach (T node in nodes) {
            if (indexByNode.ContainsKey(node)) {
                continue;
            }
            PushNode(node, node, hasParent: false);
            while (searchStack.Count > 0) {
                SearchFrame<T> frame = searchStack.Peek();
                if (frame.TryMoveNext(out T neighbor)) {
                    if (!indexByNode.TryGetValue(neighbor, out int neighborIndex)) {
                        PushNode(neighbor, frame.Node, hasParent: true);
                    } else if (onComponentStack.Contains(neighbor)) {
                        // Back/cross edges to open nodes lower this node's reachable root index.
                        lowLinkByNode[frame.Node] = Math.Min(lowLinkByNode[frame.Node], neighborIndex);
                    }
                    continue;
                }

                searchStack.Pop();
                if (lowLinkByNode[frame.Node] == indexByNode[frame.Node]) {
                    // A node whose low-link equals its discovery index is the root of one SCC.
                    int component = nextComponent++;
                    T componentNode;
                    do {
                        componentNode = componentStack.Pop();
                        onComponentStack.Remove(componentNode);
                        componentByNode[componentNode] = component;
                    } while (!EqualityComparer<T>.Default.Equals(componentNode, frame.Node));
                }

                if (frame.HasParent) {
                    // Completing a DFS child propagates its best reachable root back to the parent.
                    lowLinkByNode[frame.Parent] = Math.Min(lowLinkByNode[frame.Parent], lowLinkByNode[frame.Node]);
                }
            }
        }
        return componentByNode;

        void PushNode(T node, T parent, bool hasParent) {
            indexByNode[node] = nextIndex;
            lowLinkByNode[node] = nextIndex;
            nextIndex++;
            componentStack.Push(node);
            onComponentStack.Add(node);
            searchStack.Push(new SearchFrame<T>(node, getNeighbors(node).ToList(), parent, hasParent));
        }
    }

    private sealed class SearchFrame<T>(T node, IReadOnlyList<T> neighbors, T parent, bool hasParent) {
        private int _nextNeighborIndex;

        public T Node { get; } = node;
        public T Parent { get; } = parent;
        public bool HasParent { get; } = hasParent;

        public bool TryMoveNext(out T neighbor) {
            if (_nextNeighborIndex >= neighbors.Count) {
                neighbor = Node;
                return false;
            }
            neighbor = neighbors[_nextNeighborIndex];
            _nextNeighborIndex++;
            return true;
        }
    }
}