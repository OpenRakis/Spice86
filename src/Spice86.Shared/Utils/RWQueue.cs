namespace Spice86.Shared.Utils;

using System;
using System.Threading;

/// <summary>
/// A fixed-size thread-safe queue that blocks both the producer until space is
/// available, and the consumer until items are available.
/// Port of DOSBox Staging's <c>RWQueue&lt;T&gt;</c> from <c>src/utils/rwqueue.h</c>
/// and <c>src/misc/rwqueue.cpp</c>.
/// </summary>
/// <remarks>
/// For optimal performance inside the rwqueue, blocking is accomplished by
/// putting the thread into the waiting state, then waking it up via notify when
/// the required conditions are met.
/// Producer and consumer thread(s) are expected to simply call the enqueue and
/// dequeue methods directly without any thread state management.
/// Uses a circular buffer internally for O(1) enqueue/dequeue operations.
/// </remarks>
/// <typeparam name="T">The element type stored in the queue.</typeparam>
public sealed class RWQueue<T> {
    // Circular buffer implementation matching std::deque semantics
    private T[] _buffer;
    private int _head;  // Read position (front of queue)
    private int _tail;  // Write position (back of queue)
    private int _count; // Number of items in queue
    private readonly object _mutex = new();
    private int _capacity;
    private bool _isRunning = true;

    /// <summary>
    /// Initializes a new queue with the specified capacity.
    /// </summary>
    /// <param name="capacity">Maximum number of items the queue can hold.</param>
    public RWQueue(int capacity) {
        _buffer = new T[capacity];
        _capacity = capacity;
    }

    /// <summary>
    /// Changes the capacity of the queue.
    /// This operation reallocates the buffer if capacity changes.
    /// </summary>
    /// <param name="newCapacity">The new capacity.</param>
    public void Resize(int newCapacity) {
        lock (_mutex) {
            if (newCapacity == _capacity) {
                return;
            }
            T[] newBuffer = new T[newCapacity];
            int itemsToCopy = Math.Min(_count, newCapacity);
            for (int i = 0; i < itemsToCopy; i++) {
                newBuffer[i] = _buffer[(_head + i) % _buffer.Length];
            }
            _buffer = newBuffer;
            _head = 0;
            _tail = itemsToCopy;
            _count = itemsToCopy;
            _capacity = newCapacity;
        }
    }

    /// <summary>
    /// Returns the current number of items in the queue.
    /// </summary>
    public int Size {
        get {
            lock (_mutex) {
                return _count;
            }
        }
    }

    /// <summary>
    /// Returns the maximum capacity of the queue.
    /// </summary>
    public int MaxCapacity {
        get {
            lock (_mutex) {
                return _capacity;
            }
        }
    }

    /// <summary>
    /// Returns true if the queue contains no items.
    /// </summary>
    public bool IsEmpty {
        get {
            lock (_mutex) {
                return _count == 0;
            }
        }
    }

    /// <summary>
    /// Returns true if the queue is at capacity.
    /// </summary>
    public bool IsFull {
        get {
            lock (_mutex) {
                return _count >= _capacity;
            }
        }
    }

    /// <summary>
    /// Returns true if the queue has not been stopped.
    /// </summary>
    public bool IsRunning {
        get {
            lock (_mutex) {
                return _isRunning;
            }
        }
    }

    /// <summary>
    /// Returns the fill percentage of the queue (0–100).
    /// </summary>
    public float GetPercentFull() {
        float curLevel = Size;
        float maxLevel = _capacity;
        return 100.0f * curLevel / maxLevel;
    }

    /// <summary>
    /// Re-enables the queue after a <see cref="Stop" /> call.
    /// </summary>
    public void Start() {
        lock (_mutex) {
            _isRunning = true;
        }
    }

    /// <summary>
    /// Stops the queue, unblocking any waiting producers and consumers.
    /// </summary>
    public void Stop() {
        lock (_mutex) {
            if (!_isRunning) {
                return;
            }
            _isRunning = false;
            Monitor.PulseAll(_mutex);
        }
    }

    /// <summary>
    /// Removes all items from the queue and notifies waiting producers.
    /// </summary>
    public void Clear() {
        lock (_mutex) {
            _head = 0;
            _tail = 0;
            _count = 0;
            Monitor.PulseAll(_mutex);
        }
    }

    /// <summary>
    /// Blocking enqueue of a single item. Waits until space is available
    /// or the queue is stopped.
    /// </summary>
    /// <param name="item">The item to enqueue.</param>
    /// <returns>True if the item was enqueued; false if the queue was stopped.</returns>
    public bool Enqueue(T item) {
        lock (_mutex) {
            while (_isRunning && _count >= _capacity) {
                Monitor.Wait(_mutex);
            }

            if (_isRunning) {
                _buffer[_tail] = item;
                _tail = (_tail + 1) % _capacity;
                _count++;
                Monitor.Pulse(_mutex);
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Non-blocking enqueue of a single item.
    /// </summary>
    /// <param name="item">The item to enqueue.</param>
    /// <returns>True if the item was enqueued; false otherwise.</returns>
    public bool NonblockingEnqueue(T item) {
        lock (_mutex) {
            if (!_isRunning || _count >= _capacity) {
                return false;
            }
            _buffer[_tail] = item;
            _tail = (_tail + 1) % _capacity;
            _count++;
            Monitor.Pulse(_mutex);
            return true;
        }
    }

    /// <summary>
    /// Blocking dequeue of a single item. Waits until an item is available.
    /// </summary>
    /// <param name="success">Set to true if an item was dequeued.</param>
    /// <returns>The dequeued item, or default if none available.</returns>
    public T Dequeue(out bool success) {
        lock (_mutex) {
            while (_isRunning && _count == 0) {
                Monitor.Wait(_mutex);
            }

            if (_count > 0) {
                T item = _buffer[_head];
                _buffer[_head] = default!; // Clear reference for GC
                _head = (_head + 1) % _capacity;
                _count--;
                Monitor.Pulse(_mutex);
                success = true;
                return item;
            }

            success = false;
            return default!;
        }
    }

    /// <summary>
    /// Blocking bulk enqueue from a span.
    /// </summary>
    /// <param name="source">Source span to read items from.</param>
    /// <returns>Number of items actually enqueued.</returns>
    public int BulkEnqueue(ReadOnlySpan<T> source) {
        return BulkEnqueue(source, source.Length);
    }

    /// <summary>
    /// Blocking bulk enqueue. Moves items from source into the queue.
    /// </summary>
    /// <param name="source">Source span to read items from.</param>
    /// <param name="numRequested">Number of items to enqueue.</param>
    /// <returns>Number of items actually enqueued.</returns>
    public int BulkEnqueue(ReadOnlySpan<T> source, int numRequested) {
        const int minItems = 1;
        int sourceOffset = 0;
        int numRemaining = numRequested;

        while (numRemaining > 0) {
            lock (_mutex) {
                int freeCapacity = _capacity - _count;
                int numItems = Math.Clamp(freeCapacity, minItems, numRemaining);

                while (_isRunning && _capacity - _count < numItems) {
                    Monitor.Wait(_mutex);
                }

                if (_isRunning) {
                    numItems = Math.Min(_capacity - _count, numRemaining);

                    // Copy items into circular buffer, handling wrap-around
                    for (int i = 0; i < numItems; i++) {
                        _buffer[_tail] = source[sourceOffset + i];
                        _tail = (_tail + 1) % _capacity;
                    }
                    _count += numItems;

                    Monitor.Pulse(_mutex);
                    sourceOffset += numItems;
                    numRemaining -= numItems;
                } else {
                    break;
                }
            }
        }

        return numRequested - numRemaining;
    }

    /// <summary>
    /// Blocking bulk enqueue from an array segment.
    /// </summary>
    /// <param name="data">Source array.</param>
    /// <param name="offset">Starting offset in the source array.</param>
    /// <param name="count">Number of items to enqueue.</param>
    /// <returns>Number of items actually enqueued.</returns>
    public int BulkEnqueue(T[] data, int offset, int count) {
        return BulkEnqueue(data.AsSpan(offset, count), count);
    }

    /// <summary>
    /// Non-blocking bulk enqueue from a span.
    /// </summary>
    /// <param name="source">Source span to read items from.</param>
    /// <returns>Number of items actually enqueued.</returns>
    public int NonblockingBulkEnqueue(ReadOnlySpan<T> source) {
        return NonblockingBulkEnqueue(source, source.Length);
    }

    /// <summary>
    /// Non-blocking bulk enqueue.
    /// </summary>
    /// <param name="source">Source span to read items from.</param>
    /// <param name="numRequested">Number of items to enqueue.</param>
    /// <returns>Number of items actually enqueued.</returns>
    public int NonblockingBulkEnqueue(ReadOnlySpan<T> source, int numRequested) {
        lock (_mutex) {
            if (!_isRunning || _count >= _capacity) {
                return 0;
            }

            int availableCapacity = _capacity - _count;
            int numItems = Math.Min(availableCapacity, numRequested);

            for (int i = 0; i < numItems; i++) {
                _buffer[_tail] = source[i];
                _tail = (_tail + 1) % _capacity;
            }
            _count += numItems;

            Monitor.Pulse(_mutex);
            return numItems;
        }
    }

    /// <summary>
    /// Blocking bulk dequeue into a span. O(1) per item using circular buffer.
    /// </summary>
    /// <param name="target">Destination span for dequeued items.</param>
    /// <param name="numRequested">Number of items to dequeue.</param>
    /// <returns>Number of items actually dequeued.</returns>
    public int BulkDequeue(Span<T> target, int numRequested) {
        int targetOffset = 0;
        int numRemaining = numRequested;

        while (numRemaining > 0) {
            lock (_mutex) {
                const int minItems = 1;
                int numItems = Math.Clamp(_count, minItems, numRemaining);

                while (_isRunning && _count < numItems) {
                    Monitor.Wait(_mutex);
                }

                if (_isRunning || _count > 0) {
                    numItems = Math.Min(_count, numRemaining);

                    // Bulk copy from circular buffer to target span
                    // Handle wrap-around by copying in up to two segments
                    int firstSegment = Math.Min(numItems, _capacity - _head);
                    _buffer.AsSpan(_head, firstSegment).CopyTo(target.Slice(targetOffset));

                    int secondSegment = numItems - firstSegment;
                    if (secondSegment > 0) {
                        _buffer.AsSpan(0, secondSegment).CopyTo(target.Slice(targetOffset + firstSegment));
                    }

                    // Clear references for GC and advance head
                    for (int i = 0; i < numItems; i++) {
                        _buffer[_head] = default!;
                        _head = (_head + 1) % _capacity;
                    }
                    _count -= numItems;

                    Monitor.Pulse(_mutex);
                    targetOffset += numItems;
                    numRemaining -= numItems;
                } else {
                    break;
                }
            }
        }

        return numRequested - numRemaining;
    }

    /// <summary>
    /// Blocking bulk dequeue into an array.
    /// </summary>
    /// <param name="target">Destination array for dequeued items.</param>
    /// <param name="numRequested">Number of items to dequeue.</param>
    /// <returns>Number of items actually dequeued.</returns>
    public int BlockingBulkDequeue(T[] target, int numRequested) {
        return BulkDequeue(target, numRequested);
    }
}
