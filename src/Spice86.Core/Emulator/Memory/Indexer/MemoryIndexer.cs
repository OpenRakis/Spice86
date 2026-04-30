namespace Spice86.Core.Emulator.Memory.Indexer;

using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Utils;
using Spice86.Core.Emulator.Memory.Mmu;

using System.Collections;

public abstract class MemoryIndexer<T> : Indexer<T>, IList<T> {
    private readonly uint _accessSize;

    /// <summary>
    /// The MMU used for segmented access checks and address translation.
    /// </summary>
    protected IMmu Mmu { get; }

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="mmu">The MMU for segmented access checks.</param>
    /// <param name="accessSize">The byte size of the data type for segmented access validation.</param>
    protected MemoryIndexer(IMmu mmu, uint accessSize) {
        Mmu = mmu;
        _accessSize = accessSize;
    }

    /// <summary>
    /// Gets or sets the data at the specified segment and offset in the memory.
    /// </summary>
    /// <param name="segment">The segment of the element to get or set.</param>
    /// <param name="offset">The offset of the element to get or set.</param>
    public T this[ushort segment, uint offset] {
        get => this[segment, offset, SegmentAccessKind.Data];
        set => this[segment, offset, SegmentAccessKind.Data] = value;
    }

    /// <summary>
    /// Gets or sets the data at the specified segment and offset in the memory.
    /// </summary>
    /// <param name="segment">The segment of the element to get or set.</param>
    /// <param name="offset">The offset of the element to get or set.</param>
    public virtual T this[ushort segment, ushort offset] {
        get => this[segment, (uint)offset];
        set => this[segment, (uint)offset] = value;
    }

    /// <summary>
    /// Gets or sets the data at the specified segment and offset in the memory.
    /// Performs an MMU access check for the full data size, then delegates to
    /// <see cref="ReadSegmented"/>/<see cref="WriteSegmented"/> for translation and I/O.
    /// Composite indexers may override to perform multiple checks matching hardware access patterns.
    /// </summary>
    /// <param name="segment">The segment of the element to get or set.</param>
    /// <param name="offset">The offset of the element to get or set.</param>
    /// <param name="accessKind">The semantic access kind.</param>
    public virtual T this[ushort segment, uint offset, SegmentAccessKind accessKind] {
        get {
            Mmu.CheckAccess(segment, offset, _accessSize, accessKind);
            return ReadSegmented(segment, offset);
        }
        set {
            Mmu.CheckAccess(segment, offset, _accessSize, accessKind);
            WriteSegmented(segment, offset, value);
        }
    }

    /// <summary>
    /// Translates the segment:offset pair and reads a value without an MMU access check.
    /// Used by composite indexers that perform their own check for the full access range.
    /// </summary>
    internal abstract T ReadSegmented(ushort segment, uint offset);

    /// <summary>
    /// Translates the segment:offset pair and writes a value without an MMU access check.
    /// Used by composite indexers that perform their own check for the full access range.
    /// </summary>
    internal abstract void WriteSegmented(ushort segment, uint offset, T value);

    /// <summary>
    /// Gets or sets the data at the specified segmented address and offset in the memory.
    /// </summary>
    /// <param name="address">Segmented address at which to access the data</param>
    public T this[SegmentedAddress address] {
        get => this[address.Segment, address.Offset];
        set => this[address.Segment, address.Offset] = value;
    }

    /// <inheritdoc />
    public IEnumerator<T> GetEnumerator() {
        for (int i = 0; i < Count; i++) {
            yield return this[i];
        }
    }

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator() {
        return GetEnumerator();
    }

    /// <inheritdoc />
    public void Add(T item) => throw new NotImplementedException();

    /// <inheritdoc />
    public void Clear() => throw new NotImplementedException();

    /// <inheritdoc />
    public bool Contains(T item) {
        return IndexOf(item) != -1;
    }

    /// <inheritdoc />
    public void CopyTo(T[] array, int arrayIndex) {
        for (int i = 0; i < Count; i++) {
            array[arrayIndex + i] = this[i];
        }
    }

    /// <inheritdoc />
    public bool Remove(T item) => throw new NotImplementedException();

    /// <inheritdoc />
    public abstract int Count { get; }

    /// <inheritdoc />
    public bool IsReadOnly => false;

    /// <inheritdoc />
    public int IndexOf(T item) {
        for (int i = 0; i < Count; i++) {
            if (EqualityComparer<T>.Default.Equals(this[i], item)) {
                return i;
            }
        }

        return -1;
    }

    /// <inheritdoc />
    public void Insert(int index, T item) => throw new NotImplementedException();

    /// <inheritdoc />
    public void RemoveAt(int index) => throw new NotImplementedException();
}