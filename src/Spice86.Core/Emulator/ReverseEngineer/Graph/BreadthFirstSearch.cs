namespace Spice86.Core.Emulator.ReverseEngineer.Graph;

using System.Collections.Generic;

/// <summary>
/// Queue-based breadth-first search utilities.
/// </summary>
public static class BreadthFirstSearch {
    /// <summary>
    /// Enumerates all nodes reachable from <paramref name="seed"/> in breadth-first order.
    /// Each node is yielded at most once (tracked via <paramref name="visited"/>).
    /// </summary>
    /// <typeparam name="T">Node type.</typeparam>
    /// <param name="seed">The starting node.</param>
    /// <param name="getNeighbors">Returns the neighbors of a given node.</param>
    /// <param name="visited">Set that tracks already-visited nodes. Callers may pre-populate it to exclude nodes.</param>
    public static IEnumerable<T> Enumerate<T>(T seed, Func<T, IEnumerable<T>> getNeighbors, HashSet<T> visited) {
        return GraphTraversal.Enumerate(seed, getNeighbors, visited, new QueueFrontier<T>());
    }

    /// <summary>
    /// Enumerates all nodes reachable from <paramref name="seed"/> in breadth-first order.
    /// </summary>
    public static IEnumerable<T> Enumerate<T>(T seed, Func<T, IEnumerable<T>> getNeighbors) where T : notnull {
        return Enumerate(seed, getNeighbors, new HashSet<T>());
    }

    /// <summary>
    /// Enumerates all nodes reachable from any of the <paramref name="seeds"/> in breadth-first order.
    /// All seeds are enqueued before traversal begins, preserving true BFS layering across all starting points.
    /// Each node is yielded at most once.
    /// </summary>
    public static IEnumerable<T> Enumerate<T>(IEnumerable<T> seeds, Func<T, IEnumerable<T>> getNeighbors) where T : notnull {
        return Enumerate(seeds, getNeighbors, new HashSet<T>());
    }

    /// <summary>
    /// Enumerates all nodes reachable from any of the <paramref name="seeds"/> in breadth-first order.
    /// All seeds are enqueued before traversal begins, preserving true BFS layering across all starting points.
    /// Each node is yielded at most once (tracked via <paramref name="visited"/>).
    /// </summary>
    public static IEnumerable<T> Enumerate<T>(IEnumerable<T> seeds, Func<T, IEnumerable<T>> getNeighbors, HashSet<T> visited) {
        return GraphTraversal.Enumerate(seeds, getNeighbors, visited, new QueueFrontier<T>());
    }
}
