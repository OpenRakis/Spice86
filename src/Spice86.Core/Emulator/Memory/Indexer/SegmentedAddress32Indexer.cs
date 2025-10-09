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

    /// <summary>
    /// Gets or sets the data at the specified segment and offset in the memory.
    /// </summary>
    /// <param name="segment">The segment of the element to get or set.</param>
    /// <param name="offset">The offset of the element to get or set.</param>
    public override SegmentedAddress this[ushort segment, ushort offset] {
        get {
            uint offsetAddr = MemoryUtils.ToPhysicalAddress(segment, offset);
            uint segmentAddr = MemoryUtils.ToPhysicalAddress(segment, (ushort)(offset + 4));
            return new(_uInt16Indexer[segmentAddr], (ushort)_uInt32Indexer[offsetAddr]);
        }
        set {
            uint offsetAddr = MemoryUtils.ToPhysicalAddress(segment, offset);
            uint segmentAddr = MemoryUtils.ToPhysicalAddress(segment, (ushort)(offset + 4));
            _uInt32Indexer[offsetAddr] = value.Offset;
            _uInt16Indexer[segmentAddr] = value.Segment;
        }
    }

    /// <summary>
    /// Gets or sets the data at the specified segmented address and offset in the memory.
    /// </summary>
    /// <param name="address">Segmented address at which to access the data</param>
    public override SegmentedAddress this[SegmentedAddress address] {
        get => this[address.Segment, address.Offset];
        set => this[address.Segment, address.Offset] = value;
    }
    
    /// <inheritdoc/>
    public override int Count => _uInt16Indexer.Count / 3;
}