namespace Spice86.Core.Emulator.Memory.Indexer;

using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Provides indexed signed byte access over memory.
/// </summary>
public class Int8Indexer : MemoryIndexer<sbyte> {
    private readonly UInt8Indexer _uInt8Indexer;

    /// <summary>
    /// Initializes a new instance of the <see cref="Int8Indexer"/> class with the specified uInt8Indexer.
    /// </summary>
    /// <param name="uInt8Indexer">Where data is read and written.</param>
    public Int8Indexer(UInt8Indexer uInt8Indexer) => _uInt8Indexer = uInt8Indexer;

    /// <inheritdoc/>
    public override sbyte this[uint address] {
        get => (sbyte)_uInt8Indexer[address];
        set => _uInt8Indexer[address] = (byte)value;
    }

    /// <summary>
    /// Gets or sets the data at the specified segment and offset in the memory.
    /// </summary>
    /// <param name="segment">The segment of the element to get or set.</param>
    /// <param name="offset">The offset of the element to get or set.</param>
    public new sbyte this[ushort segment, ushort offset] {
        get => (sbyte)_uInt8Indexer[segment, offset];
        set => _uInt8Indexer[segment, offset] = (byte)value;
    }

    /// <summary>
    /// Gets or sets the data at the specified segmented address and offset in the memory.
    /// </summary>
    /// <param name="address">Segmented address at which to access the data</param>
    public new sbyte this[SegmentedAddress address] {
        get => this[address.Segment, address.Offset];
        set => this[address.Segment, address.Offset] = value;
    }
    
    /// <inheritdoc/>
    public override int Count => _uInt8Indexer.Count;
}