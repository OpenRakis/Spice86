namespace Spice86.Core.Emulator.Memory.Indexer;

using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Provides indexed signed int access over memory.
/// </summary>
public class Int32Indexer : MemoryIndexer<int> {
    private readonly UInt32Indexer _uInt32Indexer;

    /// <summary>
    /// Initializes a new instance of the <see cref="Int32Indexer"/> class with the specified uInt32Indexer.
    /// </summary>
    /// <param name="uInt32Indexer">Where data is read and written.</param>
    public Int32Indexer(UInt32Indexer uInt32Indexer) => _uInt32Indexer = uInt32Indexer;

    /// <inheritdoc/>
    public override int this[uint address] {
        get => (int)_uInt32Indexer[address];
        set => _uInt32Indexer[address] = (uint)value;
    }

    /// <summary>
    /// Gets or sets the data at the specified segment and offset in the memory.
    /// </summary>
    /// <param name="segment">The segment of the element to get or set.</param>
    /// <param name="offset">The offset of the element to get or set.</param>
    public new int this[ushort segment, ushort offset] {
        get => (int)_uInt32Indexer[segment, offset];
        set => _uInt32Indexer[segment, offset] = (uint)value;
    }

    /// <summary>
    /// Gets or sets the data at the specified segmented address and offset in the memory.
    /// </summary>
    /// <param name="address">Segmented address at which to access the data</param>
    public new int this[SegmentedAddress address] {
        get => this[address.Segment, address.Offset];
        set => this[address.Segment, address.Offset] = value;
    }
    
    /// <inheritdoc/>
    public override int Count => _uInt32Indexer.Count;
}