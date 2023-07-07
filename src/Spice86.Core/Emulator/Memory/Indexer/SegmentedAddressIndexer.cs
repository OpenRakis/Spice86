namespace Spice86.Core.Emulator.Memory.Indexer;

using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Retrieves Segment / Offset pairs stored in Memory.
///
/// Offset is the first value and Segment the second.
/// This layout is common for various instructions / interrupt table / ...
/// Instantiates objects of type SegmentedAddress for the return address.
/// </summary>
public class SegmentedAddressIndexer : Indexer<SegmentedAddress> {
    private readonly SegmentedAddressValueIndexer _segmentedAddressValueIndexer;

    public SegmentedAddressIndexer(SegmentedAddressValueIndexer segmentedAddressValueIndexer) {
        _segmentedAddressValueIndexer = segmentedAddressValueIndexer;
    }

    /// <inheritdoc/>
    public override SegmentedAddress this[uint address] {
        get => new SegmentedAddress(_segmentedAddressValueIndexer[address]);
        set => _segmentedAddressValueIndexer[address] = (value.Segment, value.Offset);
    }
}