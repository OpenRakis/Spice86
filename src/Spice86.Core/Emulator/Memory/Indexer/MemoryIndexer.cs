namespace Spice86.Core.Emulator.Memory.Indexer;

using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Utils;

using System.Collections;

public abstract class MemoryIndexer<T> : Indexer<T>, IList<T> {
    
    /// <summary>
    /// Gets or sets the data at the specified segment and offset in the memory.
    /// </summary>
    /// <param name="segment">The segment of the element to get or set.</param>
    /// <param name="offset">The offset of the element to get or set.</param>
    public T this[ushort segment, ushort offset] {
        get => this[MemoryUtils.ToPhysicalAddress(segment, offset)];
        set => this[MemoryUtils.ToPhysicalAddress(segment, offset)] = value;
    }

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