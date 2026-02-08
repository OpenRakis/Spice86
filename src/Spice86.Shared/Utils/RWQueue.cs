namespace Spice86.Shared.Utils;

using System;
using System.Threading;

/// <summary>
///     Fixed-size thread-safe queue that blocks both the producer until space is
///     available, and the consumer until items are available.
///     Port of DOSBox Staging's <c>RWQueue&lt;T&gt;</c> from <c>src/utils/rwqueue.h</c>
///     and <c>src/misc/rwqueue.cpp</c>.
/// </summary>
/// <typeparam name="T">The element type stored in the queue.</typeparam>
public sealed class RWQueue<T> {
    private readonly T[] _buffer;
    private int _head;
    private int _tail;
    private int _count;
    private int _capacity;
    private bool _isRunning = true;
    private readonly object _lock = new();

    /// <summary>
    ///     Initializes a new queue with the specified capacity.
    /// </summary>
    /// <param name="capacity">Maximum number of items the queue can hold.</param>
    public RWQueue(int capacity) {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _capacity = capacity;
        _buffer = new T[capacity];
    }

    /// <summary>
    ///     Changes the logical capacity of the queue.
    ///     Items beyond the new capacity are discarded.
    /// </summary>
    public void Resize(int newCapacity) {
        lock (_lock) {
            if (newCapacity <= 0 || newCapacity > _buffer.Length) {
                return;
            }
            _capacity = newCapacity;
            while (_count > _capacity) {
                _head = (_head + 1) % _buffer.Length;
                _count--;
            }
        }
    }

    /// <summary>
    ///     Returns the current number of items in the queue.
    /// </summary>
    public int Size {
        get {
            lock (_lock) {
                return _count;
            }
        }
    }

    /// <summary>
    ///     Returns the maximum capacity of the queue.
    /// </summary>
    public int MaxCapacity {
        get {
            lock (_lock) {
                return _capacity;
            }
        }
    }

    /// <summary>
    ///     Returns true if the queue contains no items.
    /// </summary>
    public bool IsEmpty {
        get {
            lock (_lock) {
                return _count == 0;
            }
        }
    }

    /// <summary>
    ///     Returns true if the queue is at capacity.
    /// </summary>
    public bool IsFull {
        get {
            lock (_lock) {
                return _count >= _capacity;
            }
        }
    }

    /// <summary>
    ///     Returns true if the queue has not been stopped.
    /// </summary>
    public bool IsRunning {
        get {
            lock (_lock) {
                return _isRunning;
            }
        }
    }

    /// <summary>
    ///     Returns the fill percentage of the queue (0–100).
    /// </summary>
    public float GetPercentFull() {
        float curLevel = Size;
        float maxLevel = MaxCapacity;
        return 100.0f * curLevel / maxLevel;
    }

    /// <summary>
    ///     Re-enables the queue after a <see cref="Stop" /> call.
    /// </summary>
    public void Start() {
        lock (_lock) {
            _isRunning = true;
        }
    }

    /// <summary>
    ///     Stops the queue, unblocking any waiting producers and consumers.
    /// </summary>
    public void Stop() {
        lock (_lock) {
            if (!_isRunning) {
                return;
            }
            _isRunning = false;
            Monitor.PulseAll(_lock);
        }
    }

    /// <summary>
    ///     Removes all items from the queue and notifies waiting producers.
    /// </summary>
    public void Clear() {
        lock (_lock) {
            _head = 0;
            _tail = 0;
            _count = 0;
            Monitor.PulseAll(_lock);
        }
    }

    /// <summary>
    ///     Blocking enqueue of a single item. Waits until space is available
    ///     or the queue is stopped.
    /// </summary>
    /// <returns>True if the item was enqueued; false if the queue was stopped.</returns>
    public bool Enqueue(T item) {
        lock (_lock) {
            while (_isRunning && _count >= _capacity) {
                Monitor.Wait(_lock);
            }
            if (!_isRunning) {
                return false;
            }
            _buffer[_tail] = item;
            _tail = (_tail + 1) % _buffer.Length;
            _count++;
            Monitor.PulseAll(_lock);
            return true;
        }
    }

    /// <summary>
    ///     Non-blocking enqueue of a single item.
    ///     Returns false immediately if the queue is full or stopped.
    /// </summary>
    public bool NonblockingEnqueue(T item) {
        lock (_lock) {
            if (!_isRunning || _count >= _capacity) {
                return false;
            }
            _buffer[_tail] = item;
            _tail = (_tail + 1) % _buffer.Length;
            _count++;
            Monitor.PulseAll(_lock);
            return true;
        }
    }

    /// <summary>
    ///     Blocking dequeue of a single item. Waits until an item is available.
    ///     Returns <c>default</c> with <paramref name="success" /> set to false
    ///     when the queue is stopped and empty.
    /// </summary>
    public T Dequeue(out bool success) {
        lock (_lock) {
            while (_isRunning && _count == 0) {
                Monitor.Wait(_lock);
            }
            if (_count == 0) {
                success = false;
                return default!;
            }
            T item = _buffer[_head];
            _buffer[_head] = default!;
            _head = (_head + 1) % _buffer.Length;
            _count--;
            Monitor.PulseAll(_lock);
            success = true;
            return item;
        }
    }

    /// <summary>
    ///     Blocking bulk enqueue. Moves items from <paramref name="source" /> into the
    ///     queue, blocking until all requested items have been enqueued or the queue is
    ///     stopped.
    /// </summary>
    /// <param name="source">Source span to read items from.</param>
    /// <returns>Number of items actually enqueued.</returns>
    public int BulkEnqueue(ReadOnlySpan<T> source) {
        int totalEnqueued = 0;
        // Can't use ReadOnlySpan across Monitor.Wait, copy to array if needed
        // Actually we process in chunks, each inside a single lock acquisition
        int remaining = source.Length;
        int offset = 0;

        while (remaining > 0) {
            lock (_lock) {
                while (_isRunning && _count >= _capacity) {
                    Monitor.Wait(_lock);
                }
                if (!_isRunning) {
                    return totalEnqueued;
                }

                int space = _capacity - _count;
                int toEnqueue = Math.Min(remaining, space);

                BulkCopyIn(source.Slice(offset, toEnqueue));
                _count += toEnqueue;
                totalEnqueued += toEnqueue;
                offset += toEnqueue;
                remaining -= toEnqueue;
                Monitor.PulseAll(_lock);
            }
        }
        return totalEnqueued;
    }

    /// <summary>
    ///     Blocking bulk enqueue from a float array segment (for audio interop).
    ///     Blocks until all data has been enqueued or the queue is stopped.
    /// </summary>
    /// <param name="data">Source array.</param>
    /// <param name="offset">Starting offset in the source array.</param>
    /// <param name="count">Number of items to enqueue.</param>
    /// <returns>Number of items actually enqueued.</returns>
    public int BulkEnqueue(T[] data, int offset, int count) {
        int totalEnqueued = 0;
        int remaining = count;

        while (remaining > 0) {
            lock (_lock) {
                while (_isRunning && _count >= _capacity) {
                    Monitor.Wait(_lock);
                }
                if (!_isRunning) {
                    return totalEnqueued;
                }

                int space = _capacity - _count;
                int toEnqueue = Math.Min(remaining, space);

                BulkCopyIn(data.AsSpan(offset, toEnqueue));
                _count += toEnqueue;
                totalEnqueued += toEnqueue;
                offset += toEnqueue;
                remaining -= toEnqueue;
                Monitor.PulseAll(_lock);
            }
        }
        return totalEnqueued;
    }

    /// <summary>
    ///     Non-blocking bulk enqueue. Enqueues as many items as possible without
    ///     waiting. Items that cannot fit are silently dropped.
    /// </summary>
    /// <param name="source">Source span to read items from.</param>
    /// <returns>Number of items actually enqueued.</returns>
    public int NonblockingBulkEnqueue(ReadOnlySpan<T> source) {
        lock (_lock) {
            if (!_isRunning || _count >= _capacity) {
                return 0;
            }
            int space = _capacity - _count;
            int toEnqueue = Math.Min(source.Length, space);
            BulkCopyIn(source.Slice(0, toEnqueue));
            _count += toEnqueue;
            Monitor.PulseAll(_lock);
            return toEnqueue;
        }
    }

    /// <summary>
    ///     Non-blocking bulk dequeue. Dequeues up to <paramref name="maxItems" />
    ///     items into <paramref name="target" />, returning the count actually dequeued.
    ///     Never blocks — returns 0 if the queue is empty.
    /// </summary>
    /// <param name="target">Destination array for dequeued items.</param>
    /// <param name="maxItems">Maximum number of items to dequeue.</param>
    /// <returns>Number of items actually dequeued.</returns>
    public int BulkDequeue(T[] target, int maxItems) {
        lock (_lock) {
            int toDequeue = Math.Min(maxItems, _count);
            if (toDequeue == 0) {
                return 0;
            }
            BulkCopyOut(target.AsSpan(0, toDequeue));
            _count -= toDequeue;
            Monitor.PulseAll(_lock);
            return toDequeue;
        }
    }

    /// <summary>
    ///     Blocking bulk dequeue. Waits until the requested number of items
    ///     are available, or the queue is stopped.
    /// </summary>
    /// <param name="target">Destination array for dequeued items.</param>
    /// <param name="numRequested">Number of items to dequeue.</param>
    /// <returns>Number of items actually dequeued.</returns>
    public int BlockingBulkDequeue(T[] target, int numRequested) {
        int totalDequeued = 0;
        int remaining = numRequested;

        while (remaining > 0) {
            lock (_lock) {
                int toDequeue = Math.Min(Math.Max(_count, 1), remaining);

                while ((_isRunning || _count > 0) && _count < toDequeue) {
                    Monitor.Wait(_lock);
                }

                if (_count == 0) {
                    return totalDequeued;
                }

                toDequeue = Math.Min(_count, remaining);
                BulkCopyOut(target.AsSpan(totalDequeued, toDequeue));
                _count -= toDequeue;
                totalDequeued += toDequeue;
                remaining -= toDequeue;
                Monitor.PulseAll(_lock);
            }
        }
        return totalDequeued;
    }

    /// <summary>
    ///     Copies items from <paramref name="source"/> into the ring buffer at _tail,
    ///     handling wrap-around with at most two bulk copies.
    ///     Caller must hold _lock and ensure sufficient capacity.
    /// </summary>
    private void BulkCopyIn(ReadOnlySpan<T> source) {
        int toEnqueue = source.Length;
        int firstChunk = Math.Min(toEnqueue, _buffer.Length - _tail);
        source.Slice(0, firstChunk).CopyTo(_buffer.AsSpan(_tail, firstChunk));
        int secondChunk = toEnqueue - firstChunk;
        if (secondChunk > 0) {
            source.Slice(firstChunk, secondChunk).CopyTo(_buffer.AsSpan(0, secondChunk));
        }
        _tail = (_tail + toEnqueue) % _buffer.Length;
    }

    /// <summary>
    ///     Copies items from the ring buffer at _head into <paramref name="target"/>,
    ///     handling wrap-around with at most two bulk copies, and clears the vacated slots.
    ///     Caller must hold _lock and ensure sufficient items are available.
    /// </summary>
    private void BulkCopyOut(Span<T> target) {
        int toDequeue = target.Length;
        int firstChunk = Math.Min(toDequeue, _buffer.Length - _head);
        _buffer.AsSpan(_head, firstChunk).CopyTo(target.Slice(0, firstChunk));
        int secondChunk = toDequeue - firstChunk;
        if (secondChunk > 0) {
            _buffer.AsSpan(0, secondChunk).CopyTo(target.Slice(firstChunk, secondChunk));
        }
        // Clear vacated slots to allow GC of reference types
        _buffer.AsSpan(_head, firstChunk).Clear();
        if (secondChunk > 0) {
            _buffer.AsSpan(0, secondChunk).Clear();
        }
        _head = (_head + toDequeue) % _buffer.Length;
    }
}
