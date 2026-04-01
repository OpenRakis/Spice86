namespace Spice86.Shared.Collections;

using System;
using System.Runtime.CompilerServices;

/// <summary>
/// A min-heap priority queue using <see cref="double"/> priorities.
/// Nodes must extend <see cref="TimePriorityQueueNode"/>.
/// Provides O(1) <see cref="Contains"/> and O(log n) <see cref="Enqueue"/>,
/// <see cref="Dequeue"/>, <see cref="UpdatePriority"/>, and <see cref="Remove"/> operations.
/// </summary>
/// <typeparam name="T">The node type. Must extend <see cref="TimePriorityQueueNode"/>.</typeparam>
public sealed class TimePriorityQueue<T> where T : TimePriorityQueueNode {
    private int _count;
    private T?[] _nodes;

    /// <summary>
    /// Creates a new priority queue with the specified maximum capacity.
    /// </summary>
    /// <param name="maxNodes">The maximum number of nodes the queue can hold. Must be greater than zero.</param>
    public TimePriorityQueue(int maxNodes) {
        if (maxNodes <= 0) {
            throw new ArgumentOutOfRangeException(nameof(maxNodes), "Max nodes must be greater than zero.");
        }
        _count = 0;
        _nodes = new T?[maxNodes + 1];
    }

    /// <summary>
    /// The number of nodes currently in the queue.
    /// </summary>
    public int Count => _count;

    /// <summary>
    /// The maximum number of nodes the queue can hold before a resize is needed.
    /// </summary>
    public int MaxSize => _nodes.Length - 1;

    /// <summary>
    /// Returns the node with the lowest priority without removing it.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the queue is empty.</exception>
    public T First {
        get {
            if (_count <= 0) {
                throw new InvalidOperationException("Cannot access First on an empty queue.");
            }
            return _nodes[1] ?? throw new InvalidOperationException("Queue invariant broken: root node is null.");
        }
    }

    /// <summary>
    /// Adds a node to the queue with the specified priority.
    /// </summary>
    /// <param name="node">The node to add.</param>
    /// <param name="priority">The priority value. Lower values are dequeued first.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="node"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the queue is full or the node is already enqueued.</exception>
    public void Enqueue(T node, double priority) {
        if (node == null) {
            throw new ArgumentNullException(nameof(node));
        }
        if (_count >= MaxSize) {
            throw new InvalidOperationException("Queue is full — cannot enqueue node.");
        }
        if (ReferenceEquals(node.QueueOwner, this) && node.QueueIndex >= 1 && node.QueueIndex <= _count && _nodes[node.QueueIndex] == node) {
            throw new InvalidOperationException("Node is already enqueued in this queue.");
        }

        node.Priority = priority;
        node.QueueOwner = this;
        _count++;
        _nodes[_count] = node;
        node.QueueIndex = _count;
        CascadeUp(node);
    }

    /// <summary>
    /// Removes and returns the node with the lowest priority.
    /// </summary>
    /// <returns>The node with the lowest priority.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the queue is empty.</exception>
    public T Dequeue() {
        if (_count <= 0) {
            throw new InvalidOperationException("Cannot dequeue from an empty queue.");
        }

        T returnNode = _nodes[1] ?? throw new InvalidOperationException("Queue invariant broken: root node is null.");
        if (_count == 1) {
            _nodes[1] = null;
            _count = 0;
            returnNode.QueueIndex = -1;
            returnNode.QueueOwner = null;
            return returnNode;
        }

        T lastNode = _nodes[_count] ?? throw new InvalidOperationException("Queue invariant broken: last node is null.");
        _nodes[1] = lastNode;
        lastNode.QueueIndex = 1;
        _nodes[_count] = null;
        _count--;

        CascadeDown(lastNode);

        returnNode.QueueIndex = -1;
        returnNode.QueueOwner = null;
        return returnNode;
    }

    /// <summary>
    /// Removes all nodes from the queue.
    /// </summary>
    public void Clear() {
        for (int i = 1; i <= _count; i++) {
            T? node = _nodes[i];
            if (node != null) {
                node.QueueIndex = -1;
                node.QueueOwner = null;
            }
            _nodes[i] = null;
        }
        _count = 0;
    }

    /// <summary>
    /// Determines whether the specified node is currently in this queue. O(1).
    /// </summary>
    /// <param name="node">The node to check.</param>
    /// <returns><c>true</c> if the node is in this queue; otherwise, <c>false</c>.</returns>
    public bool Contains(T node) {
        if (node == null) {
            throw new ArgumentNullException(nameof(node));
        }
        if (node.QueueIndex < 1 || node.QueueIndex > _count) {
            return false;
        }
        return _nodes[node.QueueIndex] == node;
    }

    /// <summary>
    /// Updates the priority of a node that is already in the queue.
    /// </summary>
    /// <param name="node">The node whose priority to update.</param>
    /// <param name="priority">The new priority value.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="node"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the node is not in this queue.</exception>
    public void UpdatePriority(T node, double priority) {
        if (node == null) {
            throw new ArgumentNullException(nameof(node));
        }
        if (!Contains(node)) {
            throw new InvalidOperationException("Cannot update priority of a node that is not in this queue.");
        }

        double oldPriority = node.Priority;
        node.Priority = priority;

        if (priority < oldPriority) {
            CascadeUp(node);
        } else if (priority > oldPriority) {
            CascadeDown(node);
        }
    }

    /// <summary>
    /// Removes a specific node from the queue.
    /// Attempts O(log n) removal using the node's stored index. Falls back to an O(n)
    /// linear scan if the index is stale.
    /// </summary>
    /// <param name="node">The node to remove.</param>
    /// <returns><c>true</c> if the node was found and removed; <c>false</c> otherwise.</returns>
    public bool Remove(T node) {
        if (node == null) {
            throw new ArgumentNullException(nameof(node));
        }

        // Fast path: node's QueueIndex points to the correct slot.
        if (node.QueueIndex >= 1 && node.QueueIndex <= _count && _nodes[node.QueueIndex] == node) {
            RemoveAt(node.QueueIndex);
            return true;
        }

        // Slow fallback: linear scan for stale index.
        for (int i = 1; i <= _count; i++) {
            if (_nodes[i] == node) {
                RemoveAt(i);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns the node at the given 1-based heap index. Useful for iterating over all nodes.
    /// Index 1 is the root (minimum priority); indices 2 through <see cref="Count"/> are the remaining nodes.
    /// </summary>
    /// <param name="index">A 1-based index into the internal heap array.</param>
    /// <returns>The node at the specified index.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="index"/> is out of range.</exception>
    public T NodeAt(int index) {
        if (index < 1 || index > _count) {
            throw new ArgumentOutOfRangeException(nameof(index),
                $"Index {index} is out of range. Valid range: 1 to {_count}.");
        }
        return _nodes[index] ?? throw new InvalidOperationException($"Queue invariant broken: node at index {index} is null.");
    }

    /// <summary>
    /// Resizes the internal array to accommodate a new maximum number of nodes.
    /// All currently enqueued nodes are preserved.
    /// </summary>
    /// <param name="maxNodes">The new maximum capacity. Must be at least as large as the current <see cref="Count"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maxNodes"/> is less than 1.</exception>
    /// <exception cref="InvalidOperationException">Thrown when <paramref name="maxNodes"/> is smaller than the current count.</exception>
    public void Resize(int maxNodes) {
        if (maxNodes <= 0) {
            throw new ArgumentOutOfRangeException(nameof(maxNodes), "Max nodes must be greater than zero.");
        }
        if (maxNodes < _count) {
            throw new InvalidOperationException(
                $"Cannot resize to {maxNodes}: queue currently contains {_count} nodes.");
        }

        T?[] newArray = new T?[maxNodes + 1];
        int copyLength = Math.Min(maxNodes, _count) + 1;
        Array.Copy(_nodes, newArray, copyLength);
        _nodes = newArray;
    }

    /// <summary>
    /// Removes the node at the given 1-based heap index.
    /// </summary>
    private void RemoveAt(int index) {
        T removedNode = _nodes[index] ?? throw new InvalidOperationException("Queue invariant broken: removed node is null.");
        removedNode.QueueIndex = -1;
        removedNode.QueueOwner = null;

        if (index == _count) {
            // Removing the last element — no re-heapification needed.
            _nodes[_count] = null;
            _count--;
            return;
        }

        // Move the last node into the vacated slot.
        T lastNode = _nodes[_count] ?? throw new InvalidOperationException("Queue invariant broken: last node is null.");
        _nodes[_count] = null;
        _count--;

        _nodes[index] = lastNode;
        lastNode.QueueIndex = index;

        // Restore the heap property: try cascading up first, then down.
        int parentIndex = index >> 1;
        if (parentIndex >= 1 && lastNode.Priority < (_nodes[parentIndex] ?? throw new InvalidOperationException("Queue invariant broken: parent node is null.")).Priority) {
            CascadeUp(lastNode);
        } else {
            CascadeDown(lastNode);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void CascadeUp(T node) {
        int current = node.QueueIndex;
        int parent = current >> 1;

        while (parent >= 1) {
            T parentNode = _nodes[parent] ?? throw new InvalidOperationException("Queue invariant broken: parent node is null.");
            if (parentNode.Priority <= node.Priority) {
                break;
            }

            // Parent has higher priority value (lower priority), so move parent down.
            _nodes[current] = parentNode;
            parentNode.QueueIndex = current;

            current = parent;
            parent = current >> 1;
        }

        _nodes[current] = node;
        node.QueueIndex = current;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void CascadeDown(T node) {
        int current = node.QueueIndex;

        while (true) {
            int leftChild = current << 1;
            if (leftChild > _count) {
                break;
            }

            // Find the child with the smallest priority.
            int smallestChild = leftChild;
            int rightChild = leftChild + 1;
            if (rightChild <= _count && (_nodes[rightChild] ?? throw new InvalidOperationException("Queue invariant broken: right child node is null.")).Priority < (_nodes[leftChild] ?? throw new InvalidOperationException("Queue invariant broken: left child node is null.")).Priority) {
                smallestChild = rightChild;
            }

            T childNode = _nodes[smallestChild] ?? throw new InvalidOperationException("Queue invariant broken: child node is null.");
            if (node.Priority <= childNode.Priority) {
                break;
            }

            // Move the smaller child up.
            _nodes[current] = childNode;
            childNode.QueueIndex = current;

            current = smallestChild;
        }

        _nodes[current] = node;
        node.QueueIndex = current;
    }
}
