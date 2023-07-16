namespace Spice86.Core.Emulator.Memory.Indexer;

/// <summary>
/// Retrieves Segment / Offset pairs stored in Memory.
///
/// Offset is the first value and Segment the second.
/// This layout is common for various instructions / interrupt table / ...
/// Does not instantiates an object, returns a ValueTuple.
/// </summary>
public class SegmentedAddressValueIndexer : Indexer<(ushort Segment, ushort Offset)> {
    private readonly UInt16Indexer _uInt16Indexer;

    public SegmentedAddressValueIndexer(UInt16Indexer uInt16Indexer) {
        _uInt16Indexer = uInt16Indexer;
    }

    /// <inheritdoc/>
    public override (ushort Segment, ushort Offset) this[uint address] {
        get => (_uInt16Indexer[address + 2], _uInt16Indexer[address]);
        set {
            _uInt16Indexer[address] = value.Offset;
            _uInt16Indexer[address + 2] = value.Segment;
        }
    }
}