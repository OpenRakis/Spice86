namespace Spice86.Core.Emulator.ReverseEngineer.Graph;

using System.Collections.Generic;

/// <summary>
/// Queue-based frontier for breadth-first traversal.
/// </summary>
public sealed class QueueFrontier<T> : IFrontier<T> {
    private readonly Queue<T> _queue = new();

    /// <inheritdoc />
    public int Count => _queue.Count;

    /// <inheritdoc />
    public void Add(T item) => _queue.Enqueue(item);

    /// <inheritdoc />
    public T Remove() => _queue.Dequeue();
}
