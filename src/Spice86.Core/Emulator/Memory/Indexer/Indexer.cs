namespace Spice86.Core.Emulator.Memory.Indexer;

/// <summary>
/// Accessor for data accessible via index or segmented address.
/// </summary>
/// <typeparam name="T"></typeparam>
public abstract class Indexer<T> {
    /// <summary>
    /// Gets or sets the data at the specified index in the memory.
    /// </summary>
    /// <param name="address">The index of the element to get or set.</param>
    public abstract T this[uint address] { get; set; }

    /// <summary>
    /// Indexer for addresses that are int. For convenience.
    /// </summary>
    /// <param name="address">The linear address for the element.</param>
    public T this[int address] {
        get => this[(uint)address];
        set => this[(uint)address] = value;
    }

    /// <summary>
    /// Indexer for addresses that are long. For convenience.
    /// </summary>
    /// <param name="address">The linear address for the element.</param>
    public T this[long address] {
        get => this[(uint)address];
        set => this[(uint)address] = value;
    }
}