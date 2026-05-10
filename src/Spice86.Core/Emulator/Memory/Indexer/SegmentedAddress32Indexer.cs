namespace Spice86.Core.Emulator.Memory.Indexer;

using Spice86.Shared.Emulator.Memory;
using Spice86.Core.Emulator.Memory.Mmu;

/// <summary>
/// <para>Retrieves Segment / Offset pairs stored in Memory.</para>
/// <para>
/// Offset is the first value and Segment the second.
/// This layout is common for various instructions / interrupt table / ...
/// Instantiates objects of type SegmentedAddress32 preserving the full 32-bit offset.
/// </para>
/// <para>
/// A 32-bit far pointer is 6 bytes of data: 4-byte offset followed by 2-byte segment.
/// Hardware accesses this as two 4-byte pops (offset then padded segment), so segmented
/// access performs two separate 4-byte MMU checks with offset wrapping between them,
/// then delegates to the underlying UInt32 and UInt16 indexers for translation and I/O.
/// </para>
/// </summary>
public class SegmentedAddress32Indexer : MemoryIndexer<SegmentedAddress32> {
    private readonly UInt16Indexer _uInt16Indexer;
    private readonly UInt32Indexer _uInt32Indexer;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="uInt16Indexer">The class that provides indexed unsigned 16-bit integer access over memory.</param>
    /// <param name="uInt32Indexer">The class that provides indexed unsigned 32-bit integer access over memory.</param>
    /// <param name="mmu">The MMU for access checks.</param>
    public SegmentedAddress32Indexer(UInt16Indexer uInt16Indexer, UInt32Indexer uInt32Indexer, IMmu mmu) : base(mmu, 8) {
        _uInt16Indexer = uInt16Indexer;
        _uInt32Indexer = uInt32Indexer;
    }

    /// <inheritdoc/>
    public override SegmentedAddress32 this[uint address] {
        get => new((ushort)_uInt32Indexer[address + 4], _uInt32Indexer[address]);
        set {
            _uInt32Indexer[address] = value.Offset;
            _uInt16Indexer[address + 4] = value.Segment;
        }
    }

    /// <summary>
    /// Performs two separate 4-byte MMU checks matching hardware's two-pop access pattern,
    /// then delegates to <see cref="ReadSegmented"/>/<see cref="WriteSegmented"/>.
    /// </summary>
    public override SegmentedAddress32 this[ushort segment, uint offset, SegmentAccessKind accessKind] {
        get {
            Mmu.CheckAccess(segment, offset, 4, accessKind);
            // Cast to ushort: models SP register wrapping between the two hardware pops.
            // Each pop checks its own 4-byte span at the wrapped 16-bit offset.
            Mmu.CheckAccess(segment, (ushort)(offset + 4), 4, accessKind);
            return ReadSegmented(segment, offset);
        }
        set {
            Mmu.CheckAccess(segment, offset, 4, accessKind);
            Mmu.CheckAccess(segment, (ushort)(offset + 4), 4, accessKind);
            WriteSegmented(segment, offset, value);
        }
    }

    /// <inheritdoc />
    internal override SegmentedAddress32 ReadSegmented(ushort segment, uint offset) {
        uint offsetValue = _uInt32Indexer.ReadSegmented(segment, offset);
        ushort segmentValue = _uInt16Indexer.ReadSegmented(segment, offset + 4u);
        return new(segmentValue, offsetValue);
    }

    /// <inheritdoc />
    internal override void WriteSegmented(ushort segment, uint offset, SegmentedAddress32 value) {
        _uInt32Indexer.WriteSegmented(segment, offset, value.Offset);
        _uInt16Indexer.WriteSegmented(segment, offset + 4u, value.Segment);
    }
    
    /// <inheritdoc/>
    public override int Count => _uInt16Indexer.Count / 3;
}