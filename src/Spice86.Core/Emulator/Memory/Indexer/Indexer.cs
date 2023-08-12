namespace Spice86.Core.Emulator.Memory.Indexer;

using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Utils;

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
    /// <param name="address"></param>
    public T this[int address] {
        get => this[(uint)address];
        set => this[(uint)address] = value;
    }
    
    /// <summary>
    /// Indexer for addresses that are long. For convenience.
    /// </summary>
    /// <param name="address"></param>
    public T this[long address] {
        get => this[(uint)address];
        set => this[(uint)address] = value;
    }

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
}