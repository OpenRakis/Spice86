namespace Spice86.Core.Emulator.Memory.Indexer;

using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Utils;

public abstract class MemoryIndexer<T> : Indexer<T> {
    
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