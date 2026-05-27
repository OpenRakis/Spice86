namespace Spice86.Shared.Utils;

using System.Threading;

/// <summary>
/// Thread-safe, instance-scoped sequential ID counter.
/// </summary>
public sealed class SequentialIdAllocator {
    private int _nextId = -1;

    /// <summary>
    /// Initializes a new allocator with IDs starting at 0.
    /// </summary>
    public SequentialIdAllocator() {
    }

    /// <summary>
    /// Initializes a new allocator with IDs starting at <paramref name="startId"/>.
    /// </summary>
    public SequentialIdAllocator(int startId) {
        NextId = startId;
    }

    /// <summary>
    /// Allocates and returns the next available ID.
    /// </summary>
    public int AllocateId() => Interlocked.Increment(ref _nextId);

    /// <summary>
    /// Sets the next ID to be allocated. Used to resume allocation beyond a known ID.
    /// </summary>
    public int NextId {
        set => Volatile.Write(ref _nextId, value - 1);
    }
}
