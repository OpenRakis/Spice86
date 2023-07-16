namespace Spice86.Core.Emulator.ReverseEngineer.DataStructure.Array;

using Spice86.Core.Emulator.Memory.ReaderWriter;

using System.Collections;

/// <summary>
/// An abstract generic class that represents a memory-based array data structure with a base address.
/// </summary>
/// <typeparam name="T">The type of elements in the array.</typeparam>
public abstract class MemoryBasedArray<T> : MemoryBasedDataStructure, IEnumerable<T>, IList<T> {
    private readonly int _length;

    /// <summary>
    /// Initializes a new instance of the <see cref="MemoryBasedArray{T}"/> class with the specified memory, base address and length.
    /// </summary>
    /// <param name="byteReaderWriter">Where data is read and written.</param>
    /// <param name="baseAddress">The base address of the array.</param>
    /// <param name="length">The length of the array.</param>
    protected MemoryBasedArray(IByteReaderWriter byteReaderWriter, uint baseAddress, int length) : base(byteReaderWriter, baseAddress) {
        _length = length;
    }

    /// <summary>
    /// Gets the size of each element in the array.
    /// </summary>
    public abstract int ValueSize { get; }

    /// <summary>
    /// Converts the index of an element to its offset in memory.
    /// </summary>
    /// <param name="index">The zero-based index of the element.</param>
    /// <returns>The offset of the element in memory.</returns>
    public uint IndexToOffset(int index) {
        return (uint)(index * ValueSize);
    }

    /// IEnumerable implementation
    
    /// <inheritdoc/>
    public IEnumerator<T> GetEnumerator() {
        return new MemoryBasedArrayEnumerator<T>(this);
    }

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() {
        return GetEnumerator();
    }

    
    /// IList implementation

    /// <inheritdoc/>
    public abstract T this[int i] { get; set; }

    /// <inheritdoc/>
    public int IndexOf(T item) {
        for (int i = 0; i < Count; i++) {
            if (Equals(this[i], item)) {
                return i;
            }
        }
        return -1;
    }
    
    /// <inheritdoc/>
    public void Insert(int index, T item) {
        throw CreateNotSupportedExceptionReadOnly();
    }

    /// <inheritdoc/>
    public void RemoveAt(int index) {
        throw CreateNotSupportedExceptionReadOnly();
    }
    
    /// ICollection implementation

    /// <inheritdoc/>
    public bool Contains(T item) {
        return IndexOf(item) != -1;
    }

    /// <inheritdoc/>
    public void CopyTo(T[] array, int index) {
        for (int i = 0; i < Count; i++) {
            array.SetValue(this[i], index++);
        }
    }

    /// <inheritdoc/>
    public int Count { get => _length; }

    /// <inheritdoc/>
    public bool IsReadOnly => true;

    /// <inheritdoc/>
    public void Add(T item) {
        throw CreateNotSupportedExceptionReadOnly();
    }

    /// <inheritdoc/>
    public void Clear() {
        throw CreateNotSupportedExceptionReadOnly();
    }

    /// <inheritdoc/>
    public bool Remove(T item) {
        throw CreateNotSupportedExceptionReadOnly();
    }

    private NotSupportedException CreateNotSupportedExceptionReadOnly() {
        return new NotSupportedException("Not Supported: Read Only Collection");
    }
}