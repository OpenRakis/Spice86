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
public class SegmentedAddress32Indexer : MemoryIndexer<SegmentedAddress> {
    private readonly UInt16Indexer _uInt16Indexer;
    private readonly UInt32Indexer _uInt32Indexer;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="uInt16Indexer">The class that provides indexed unsigned 16-byte integer access over memory.</param>
    /// <param name="uInt32Indexer">The class that provides indexed unsigned 32-byte integer access over memory.</param>
    public SegmentedAddress32Indexer(UInt16Indexer uInt16Indexer, UInt32Indexer  uInt32Indexer) {
        _uInt16Indexer = uInt16Indexer;
        _uInt32Indexer = uInt32Indexer;
    }

    /// <inheritdoc/>
    public override SegmentedAddress this[uint address] {
        get => new(_uInt16Indexer[address + 4], (ushort)_uInt32Indexer[address]);
        set {
            _uInt32Indexer[address] = value.Offset;
            _uInt16Indexer[address + 4] = value.Segment;
        }
    }
}