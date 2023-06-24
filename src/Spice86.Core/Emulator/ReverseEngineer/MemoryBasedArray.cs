namespace Spice86.Core.Emulator.ReverseEngineer;

using Spice86.Core.Emulator.Memory;
/// <summary>
/// An abstract generic class that represents a memory-based array data structure with a base address.
/// </summary>
/// <typeparam name="T">The type of elements in the array.</typeparam>
public abstract class MemoryBasedArray<T> : MemoryBasedDataStructureWithBaseAddress {
    private readonly int _length;

    /// <summary>
    /// Initializes a new instance of the <see cref="MemoryBasedArray{T}"/> class with the specified memory, base address and length.
    /// </summary>
    /// <param name="memory">The memory that the array is based on.</param>
    /// <param name="baseAddress">The base address of the array.</param>
    /// <param name="length">The length of the array.</param>
    protected MemoryBasedArray(IMemoryStore memory, uint baseAddress, int length) : base(memory, baseAddress) {
        _length = length;
    }

    /// <summary>
    /// Gets or sets the element at the specified index.
    /// </summary>
    /// <param name="i">The zero-based index of the element to get or set.</param>
    /// <returns>The element at the specified index.</returns>
    public abstract T this[int i] { get; set; }

    /// <summary>
    /// Gets the length of the array.
    /// </summary>
    public int Length => _length;

    /// <summary>
    /// Gets the element at the specified index.
    /// </summary>
    /// <param name="index">The zero-based index of the element to get.</param>
    /// <returns>The element at the specified index.</returns>
    public abstract T GetValueAt(int index);

    /// <summary>
    /// Gets the size of each element in the array.
    /// </summary>
    public abstract int ValueSize { get; }

    /// <summary>
    /// Converts the index of an element to its offset in memory.
    /// </summary>
    /// <param name="index">The zero-based index of the element.</param>
    /// <returns>The offset of the element in memory.</returns>
    public int IndexToOffset(int index) {
        return index * ValueSize;
    }

    /// <summary>
    /// Sets the element at the specified index.
    /// </summary>
    /// <param name="index">The zero-based index of the element to set.</param>
    /// <param name="value">The value to set the element to.</param>
    public abstract void SetValueAt(int index, T value);
}