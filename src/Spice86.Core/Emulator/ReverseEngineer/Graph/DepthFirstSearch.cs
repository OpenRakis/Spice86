namespace Spice86.Core.Emulator.ReverseEngineer.Graph;

using System.Collections.Generic;

/// <summary>
/// Stack-based depth-first search utilities.
/// </summary>
public static class DepthFirstSearch {
    /// <summary>
    /// Enumerates all nodes reachable from <paramref name="seed"/> in depth-first order.
    /// Each node is yielded at most once (tracked via <paramref name="visited"/>).
    /// </summary>
    /// <typeparam name="T">Node type.</typeparam>
    /// <param name="seed">The starting node.</param>
    /// <param name="getNeighbors">Returns the neighbors of a given node.</param>
    /// <param name="visited">Set that tracks already-visited nodes. Callers may pre-populate it to exclude nodes.</param>
    public static IEnumerable<T> Enumerate<T>(T seed, Func<T, IEnumerable<T>> getNeighbors, HashSet<T> visited) {
        return GraphTraversal.Enumerate(seed, getNeighbors, visited, new StackFrontier<T>());
    }

    /// <summary>
    /// Enumerates all nodes reachable from <paramref name="seed"/> in depth-first order.
    /// </summary>
    public static IEnumerable<T> Enumerate<T>(T seed, Func<T, IEnumerable<T>> getNeighbors) where T : notnull {
        return Enumerate(seed, getNeighbors, new HashSet<T>());
    }

    /// <summary>
    /// Enumerates all nodes reachable from any of the <paramref name="seeds"/> in depth-first order.
    /// Each node is yielded at most once.
    /// </summary>
    public static IEnumerable<T> Enumerate<T>(IEnumerable<T> seeds, Func<T, IEnumerable<T>> getNeighbors) where T : notnull {
        HashSet<T> visited = new();
        foreach (T seed in seeds) {
            foreach (T node in Enumerate(seed, getNeighbors, visited)) {
                yield return node;
            }
        }
    }

    /// <summary>
    /// Returns whether <paramref name="target"/> is reachable from <paramref name="seed"/> via the given neighbor function.
    /// </summary>
    public static bool CanReach<T>(T seed, T target, Func<T, IEnumerable<T>> getNeighbors) where T : notnull {
        return GraphTraversal.CanReach(seed, target, getNeighbors, new StackFrontier<T>());
    }
}
