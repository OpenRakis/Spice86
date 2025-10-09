namespace Spice86.Core.Emulator.Memory.Indexer;

using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Provides indexed signed short access over memory.
/// </summary>
public class Int16Indexer : MemoryIndexer<short> {
    private readonly UInt16Indexer _uInt16Indexer;

    /// <summary>
    /// Initializes a new instance of the <see cref="Int16Indexer"/> class with the specified uInt16Indexer.
    /// </summary>
    /// <param name="uInt16Indexer">Where data is read and written.</param>
    public Int16Indexer(UInt16Indexer uInt16Indexer) => _uInt16Indexer = uInt16Indexer;

    /// <inheritdoc/>
    public override short this[uint address] {
        get => (short)_uInt16Indexer[address];
        set => _uInt16Indexer[address] = (ushort)value;
    }

    /// <summary>
    /// Gets or sets the data at the specified segment and offset in the memory.
    /// </summary>
    /// <param name="segment">The segment of the element to get or set.</param>
    /// <param name="offset">The offset of the element to get or set.</param>
    public override short this[ushort segment, ushort offset] {
        get => (short)_uInt16Indexer[segment, offset];
        set => _uInt16Indexer[segment, offset] = (ushort)value;
    }

    /// <summary>
    /// Gets or sets the data at the specified segmented address and offset in the memory.
    /// </summary>
    /// <param name="address">Segmented address at which to access the data</param>
    public override short this[SegmentedAddress address] {
        get => this[address.Segment, address.Offset];
        set => this[address.Segment, address.Offset] = value;
    }
    
    /// <inheritdoc/>
    public override int Count => _uInt16Indexer.Count;
}