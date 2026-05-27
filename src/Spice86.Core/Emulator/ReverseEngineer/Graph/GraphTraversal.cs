namespace Spice86.Core.Emulator.ReverseEngineer.Graph;

using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Core graph traversal engine parameterized by frontier strategy.
/// DFS uses a stack (LIFO), BFS uses a queue (FIFO).
/// </summary>
public static class GraphTraversal {
    /// <summary>
    /// Enumerates all nodes reachable from <paramref name="seed"/> using the given frontier strategy.
    /// Each node is yielded at most once (tracked via <paramref name="visited"/>).
    /// </summary>
    public static IEnumerable<T> Enumerate<T>(
        T seed,
        Func<T, IEnumerable<T>> getNeighbors,
        HashSet<T> visited,
        IFrontier<T> frontier) {
        if (!visited.Add(seed)) {
            yield break;
        }
        frontier.Add(seed);
        while (frontier.Count > 0) {
            T current = frontier.Remove();
            yield return current;
            foreach (T neighbor in getNeighbors(current).Where(n => visited.Add(n))) {
                frontier.Add(neighbor);
            }
        }
    }

    /// <summary>
    /// Enumerates all nodes reachable from any of the <paramref name="seeds"/> using the given frontier strategy.
    /// All seeds are added to the frontier before traversal begins.
    /// Each node is yielded at most once (tracked via <paramref name="visited"/>).
    /// </summary>
    public static IEnumerable<T> Enumerate<T>(
        IEnumerable<T> seeds,
        Func<T, IEnumerable<T>> getNeighbors,
        HashSet<T> visited,
        IFrontier<T> frontier) {
        foreach (T seed in seeds.Where(s => visited.Add(s))) {
            frontier.Add(seed);
        }
        while (frontier.Count > 0) {
            T current = frontier.Remove();
            yield return current;
            foreach (T neighbor in getNeighbors(current).Where(n => visited.Add(n))) {
                frontier.Add(neighbor);
            }
        }
    }

    /// <summary>
    /// Returns whether <paramref name="target"/> is reachable from <paramref name="seed"/>
    /// via the given neighbor function, using the specified frontier strategy.
    /// </summary>
    public static bool CanReach<T>(
        T seed,
        T target,
        Func<T, IEnumerable<T>> getNeighbors,
        IFrontier<T> frontier) where T : notnull {
        if (EqualityComparer<T>.Default.Equals(seed, target)) {
            return true;
        }
        HashSet<T> visited = new() { seed };
        frontier.Add(seed);
        while (frontier.Count > 0) {
            T current = frontier.Remove();
            foreach (T neighbor in getNeighbors(current)) {
                if (EqualityComparer<T>.Default.Equals(neighbor, target)) {
                    return true;
                }
                if (visited.Add(neighbor)) {
                    frontier.Add(neighbor);
                }
            }
        }
        return false;
    }
}
