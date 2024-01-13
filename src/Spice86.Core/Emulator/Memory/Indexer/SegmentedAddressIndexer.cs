namespace Spice86.Core.Emulator.Memory.Indexer;

using Spice86.Shared.Emulator.Memory;

/// <summary>
/// <para>Retrieves Segment / Offset pairs stored in Memory.</para>
/// <para>
/// Offset is the first value and Segment the second.
/// This layout is common for various instructions / interrupt table / ...
/// Instantiates objects of type SegmentedAddress for the return address.
/// </para>
/// </summary>
public class SegmentedAddressIndexer : MemoryIndexer<SegmentedAddress> {
    private readonly UInt16Indexer _uInt16Indexer;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="uInt16Indexer">The class that provides indexed unsigned 16-byte integer access over memory.</param>
    public SegmentedAddressIndexer(UInt16Indexer uInt16Indexer) {
        _uInt16Indexer = uInt16Indexer;
    }

    /// <inheritdoc/>
    public override SegmentedAddress this[uint address] {
        get => new(_uInt16Indexer[address + 2], _uInt16Indexer[address]);
        set {
            _uInt16Indexer[address] = value.Offset;
            _uInt16Indexer[address + 2] = value.Segment;
        }
    }
}