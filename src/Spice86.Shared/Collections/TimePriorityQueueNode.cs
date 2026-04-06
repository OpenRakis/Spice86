namespace Spice86.Shared.Collections;

/// <summary>
/// Base class for nodes stored in a <see cref="TimePriorityQueue{T}"/>.
/// Each node tracks its position in the heap and its priority value.
/// </summary>
public abstract class TimePriorityQueueNode {
    /// <summary>
    /// The index of this node within the backing array of the queue.
    /// A value of <c>-1</c> indicates the node is not currently in any queue.
    /// </summary>
    public int QueueIndex { get; set; } = -1;

    /// <summary>
    /// The priority of this node. Lower values indicate higher priority.
    /// </summary>
    public double Priority { get; set; }

    /// <summary>
    /// The queue instance that currently owns this node, or <c>null</c> if not enqueued.
    /// Used internally for validation.
    /// </summary>
    internal object? QueueOwner { get; set; }
}
