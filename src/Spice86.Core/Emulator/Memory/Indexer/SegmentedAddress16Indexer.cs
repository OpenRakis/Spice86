namespace Spice86.Core.Emulator.Memory.Indexer;

using Spice86.Shared.Emulator.Memory;
using Spice86.Core.Emulator.Memory.Mmu;

/// <summary>
/// <para>Retrieves Segment / Offset pairs stored in Memory.</para>
/// <para>
/// Offset is the first value and Segment the second.
/// This layout is common for various instructions / interrupt table / ...
/// Instantiates objects of type SegmentedAddress for the return address.
/// </para>
/// </summary>
public class SegmentedAddress16Indexer : MemoryIndexer<SegmentedAddress> {
    private readonly UInt16Indexer _uInt16Indexer;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="uInt16Indexer">The class that provides indexed unsigned 16-byte integer access over memory.</param>
    /// <param name="mmu">The MMU for access checks.</param>
    public SegmentedAddress16Indexer(UInt16Indexer uInt16Indexer, IMmu mmu) : base(mmu, 4) {
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

    /// <inheritdoc />
    internal override SegmentedAddress ReadSegmented(ushort segment, uint offset) {
        ushort offsetValue = _uInt16Indexer.ReadSegmented(segment, offset);
        ushort segmentValue = _uInt16Indexer.ReadSegmented(segment, offset + 2u);
        return new(segmentValue, offsetValue);
    }

    /// <inheritdoc />
    internal override void WriteSegmented(ushort segment, uint offset, SegmentedAddress value) {
        _uInt16Indexer.WriteSegmented(segment, offset, value.Offset);
        _uInt16Indexer.WriteSegmented(segment, offset + 2u, value.Segment);
    }

    /// <inheritdoc/>
    public override int Count => _uInt16Indexer.Count / 2;
}