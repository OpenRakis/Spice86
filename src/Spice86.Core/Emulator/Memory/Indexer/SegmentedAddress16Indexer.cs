namespace Spice86.Core.Emulator.Memory.Indexer;

using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Utils;

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
    public SegmentedAddress16Indexer(UInt16Indexer uInt16Indexer) {
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

    /// <summary>
    /// Gets or sets the data at the specified segment and offset in the memory.
    /// </summary>
    /// <param name="segment">The segment of the element to get or set.</param>
    /// <param name="offset">The offset of the element to get or set.</param>
    public override SegmentedAddress this[ushort segment, ushort offset] {
        get {
            ushort offsetValue = _uInt16Indexer[segment, offset];
            ushort segmentValue = _uInt16Indexer[segment, (ushort)(offset + 2)];
            return new(segmentValue, offsetValue);
        }
        set {
            _uInt16Indexer[segment, offset] = value.Offset;
            _uInt16Indexer[segment, (ushort)(offset + 2)] = value.Segment;
        }
    }

    /// <inheritdoc/>
    public override int Count => _uInt16Indexer.Count / 2;
}