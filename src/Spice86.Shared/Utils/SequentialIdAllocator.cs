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
    /// When set, <see cref="AllocateId"/> always returns this value instead of incrementing.
    /// Set to <see langword="null"/> to resume normal sequential allocation.
    /// </summary>
    public int? FixedId { get; set; }

    /// <summary>
    /// Allocates and returns the next available ID.
    /// When <see cref="FixedId"/> is set, that value is returned without advancing the counter.
    /// </summary>
    public int AllocateId() => FixedId ?? Interlocked.Increment(ref _nextId);

    /// <summary>
    /// Sets the next ID to be allocated. Used to resume allocation beyond a known ID.
    /// </summary>
    public int NextId {
        set => Volatile.Write(ref _nextId, value - 1);
    }
}
