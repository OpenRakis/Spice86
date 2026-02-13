namespace Spice86.Shared.Utils;

using System;
using System.Collections.Generic;
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
/// </remarks>
/// <typeparam name="T">The element type stored in the queue.</typeparam>
public sealed class RWQueue<T> {
    // Using List<T> similar to std::deque - dynamically growing
    private readonly List<T> _queue = new();
    private readonly object _mutex = new();
    private int _capacity;
    private bool _isRunning = true;

    /// <summary>
    /// Initializes a new queue with the specified capacity.
    /// </summary>
    /// <param name="capacity">Maximum number of items the queue can hold.</param>
    public RWQueue(int capacity) {
        Resize(capacity);
    }

    /// <summary>
    /// Changes the capacity of the queue.
    /// This is a fast operation that only sets the capacity variable.
    /// It does not drop frames or append zeros to the underlying data structure.
    /// </summary>
    /// <param name="newCapacity">The new capacity.</param>
    public void Resize(int newCapacity) {
        lock (_mutex) {
            _capacity = newCapacity;
        }
    }

    /// <summary>
    /// Returns the current number of items in the queue.
    /// </summary>
    public int Size {
        get {
            lock (_mutex) {
                return _queue.Count;
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
                return _queue.Count == 0;
            }
        }
    }

    /// <summary>
    /// Returns true if the queue is at capacity.
    /// </summary>
    public bool IsFull {
        get {
            lock (_mutex) {
                return _queue.Count >= _capacity;
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
            // notify the conditions
            Monitor.PulseAll(_mutex);
        }
    }

    /// <summary>
    /// Removes all items from the queue and notifies waiting producers.
    /// </summary>
    public void Clear() {
        lock (_mutex) {
            _queue.Clear();
        }
        lock (_mutex) {
            Monitor.PulseAll(_mutex);
        }
    }

    /// <summary>
    /// Blocking enqueue of a single item. Waits until space is available
    /// or the queue is stopped.
    /// If queuing has stopped prior to enqueing, then this will immediately
    /// return false and the item will not be queued.
    /// </summary>
    /// <param name="item">The item to enqueue.</param>
    /// <returns>True if the item was enqueued; false if the queue was stopped.</returns>
    public bool Enqueue(T item) {
        lock (_mutex) {
            // wait until we're stopped or the queue has room to accept the item
            while (_isRunning && _queue.Count >= _capacity) {
                Monitor.Wait(_mutex);
            }

            // add it, and notify the next waiting thread that we've got an item
            if (_isRunning) {
                _queue.Add(item);
                Monitor.Pulse(_mutex);
                return true;
            }
            // If we stopped while enqueing, then anything that was enqueued prior
            // to being stopped is safely in the queue.
            return false;
        }
    }

    /// <summary>
    /// Non-blocking enqueue of a single item.
    /// Returns false and does nothing if the queue is at capacity or the queue is not running.
    /// Otherwise the item gets moved into the queue and it returns true.
    /// </summary>
    /// <param name="item">The item to enqueue.</param>
    /// <returns>True if the item was enqueued; false otherwise.</returns>
    public bool NonblockingEnqueue(T item) {
        lock (_mutex) {
            if (!_isRunning || _queue.Count >= _capacity) {
                return false;
            }
            _queue.Add(item);
            Monitor.Pulse(_mutex);
            return true;
        }
    }

    /// <summary>
    /// Blocking dequeue of a single item. Waits until an item is available.
    /// If queuing has stopped, this will continue to return item(s) until
    /// none remain in the queue, at which point it returns null/default.
    /// </summary>
    /// <param name="success">Set to true if an item was dequeued; false if queue is stopped and empty.</param>
    /// <returns>The dequeued item, or default if none available.</returns>
    public T Dequeue(out bool success) {
        lock (_mutex) {
            // wait until we're stopped or the queue has an item
            while (_isRunning && _queue.Count == 0) {
                Monitor.Wait(_mutex);
            }

            // Even if the queue has stopped, we need to drain the (previously)
            // queued items before we're done.
            if (_queue.Count > 0) {
                T item = _queue[0];
                _queue.RemoveAt(0);
                // notify the first waiting thread that the queue has room
                Monitor.Pulse(_mutex);
                success = true;
                return item;
            }

            success = false;
            return default!;
        }
    }

    /// <summary>
    /// Blocking bulk enqueue. Moves items from <paramref name="source" /> into the
    /// queue, blocking until all requested items have been enqueued or the queue is
    /// stopped.
    /// If the queue is stopped then the function unblocks and returns the
    /// quantity enqueued (which can be less than the number requested).
    /// </summary>
    /// <param name="source">Source span to read items from.</param>
    /// <returns>Number of items actually enqueued.</returns>
    public int BulkEnqueue(ReadOnlySpan<T> source) {
        return BulkEnqueue(source, source.Length);
    }

    /// <summary>
    /// Blocking bulk enqueue. Moves items from <paramref name="source" /> into the
    /// queue, blocking until all requested items have been enqueued or the queue is
    /// stopped.
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
                int freeCapacity = _capacity - _queue.Count;
                int numItems = Math.Clamp(freeCapacity, minItems, numRemaining);

                // wait until we're stopped or the queue has enough room for the items
                while (_isRunning && _capacity - _queue.Count < numItems) {
                    Monitor.Wait(_mutex);
                }

                if (_isRunning) {
                    // Add items to queue
                    for (int i = 0; i < numItems; i++) {
                        _queue.Add(source[sourceOffset + i]);
                    }

                    // notify the first waiting thread that we have an item
                    Monitor.Pulse(_mutex);

                    sourceOffset += numItems;
                    numRemaining -= numItems;
                } else {
                    // If we stopped while bulk enqueing, then stop here.
                    // Anything that was enqueued prior to being stopped is
                    // safely in the queue.
                    break;
                }
            }
        }

        return numRequested - numRemaining;
    }

    /// <summary>
    /// Blocking bulk enqueue from an array segment.
    /// Blocks until all data has been enqueued or the queue is stopped.
    /// </summary>
    /// <param name="data">Source array.</param>
    /// <param name="offset">Starting offset in the source array.</param>
    /// <param name="count">Number of items to enqueue.</param>
    /// <returns>Number of items actually enqueued.</returns>
    public int BulkEnqueue(T[] data, int offset, int count) {
        return BulkEnqueue(data.AsSpan(offset, count), count);
    }

    /// <summary>
    /// Non-blocking bulk enqueue. Does nothing if queue is at capacity or queue is not running.
    /// Otherwise, enqueues as many elements as possible until the queue is at capacity.
    /// </summary>
    /// <param name="source">Source span to read items from.</param>
    /// <returns>Number of items actually enqueued.</returns>
    public int NonblockingBulkEnqueue(ReadOnlySpan<T> source) {
        return NonblockingBulkEnqueue(source, source.Length);
    }

    /// <summary>
    /// Non-blocking bulk enqueue. Does nothing if queue is at capacity or queue is not running.
    /// Otherwise, enqueues as many elements as possible until the queue is at capacity.
    /// </summary>
    /// <param name="source">Source span to read items from.</param>
    /// <param name="numRequested">Number of items to enqueue.</param>
    /// <returns>Number of items actually enqueued.</returns>
    public int NonblockingBulkEnqueue(ReadOnlySpan<T> source, int numRequested) {
        lock (_mutex) {
            if (!_isRunning || _queue.Count >= _capacity) {
                return 0;
            }

            int availableCapacity = _capacity - _queue.Count;
            int numItems = Math.Min(availableCapacity, numRequested);

            for (int i = 0; i < numItems; i++) {
                _queue.Add(source[i]);
            }

            Monitor.Pulse(_mutex);
            return numItems;
        }
    }

    /// <summary>
    /// Blocking bulk dequeue. Dequeues the requested number of items into the given container
    /// in-bulk, and returns the quantity dequeued. This potentially blocks
    /// until the requested number of items have been dequeued.
    /// If queuing was stopped prior to bulk dequeueing then this
    /// immediately returns with a value of 0 and no items dequeued.
    /// If queuing was stopped in the middle of bulk dequeueing then this
    /// immediately returns with a value indicating the subset dequeued.
    /// </summary>
    /// <param name="target">Destination array for dequeued items.</param>
    /// <param name="numRequested">Number of items to dequeue.</param>
    /// <returns>Number of items actually dequeued.</returns>
    public int BulkDequeue(T[] target, int numRequested) {
        int targetOffset = 0;
        int numRemaining = numRequested;

        while (numRemaining > 0) {
            lock (_mutex) {
                const int minItems = 1;
                int numItems = Math.Clamp(_queue.Count, minItems, numRemaining);

                // wait until we're stopped or the queue has enough items
                while (_isRunning && _queue.Count < numItems) {
                    Monitor.Wait(_mutex);
                }

                // Even if the queue has stopped, we need to drain the
                // (previously) queued items before we're done.
                if (_isRunning || _queue.Count > 0) {
                    numItems = Math.Min(_queue.Count, numRemaining);

                    // Move items to target (matching std::move behavior)
                    for (int i = 0; i < numItems; i++) {
                        target[targetOffset + i] = _queue[i];
                    }
                    _queue.RemoveRange(0, numItems);

                    // notify the first waiting thread that the queue has room
                    Monitor.Pulse(_mutex);

                    targetOffset += numItems;
                    numRemaining -= numItems;
                } else {
                    // The queue was stopped mid-dequeue!
                    break;
                }
            }
        }

        return numRequested - numRemaining;
    }

    /// <summary>
    /// Blocking bulk dequeue. Waits until the requested number of items
    /// are available, or the queue is stopped.
    /// </summary>
    /// <param name="target">Destination array for dequeued items.</param>
    /// <param name="numRequested">Number of items to dequeue.</param>
    /// <returns>Number of items actually dequeued.</returns>
    public int BlockingBulkDequeue(T[] target, int numRequested) {
        return BulkDequeue(target, numRequested);
    }
}
