namespace Spice86.Core.Emulator.Memory.Indexer;

using Spice86.Shared.Utils;

/// <summary>
/// Retrieves Segment / Offset pairs stored in Memory.
/// 
/// Offset is the first value and Segment the second.
/// This layout is common for various instructions / interrupt table / ...
/// </summary>
public class OffsetSegmentIndexer {
    private readonly UInt16Indexer _uInt16Indexer;

    public OffsetSegmentIndexer(UInt16Indexer uInt16Indexer) {
        _uInt16Indexer = uInt16Indexer;
    }

    
    /// <summary>
    /// Segmented address storage in memory.
    /// Tuple first value is Segment and second is Offset.
    /// In memory, storage is inverted so Offset is first and Segment is second.
    /// </summary>
    public (ushort Segment, ushort Offset) this[uint address] {
        get => (_uInt16Indexer[address + 2], _uInt16Indexer[address]);
        set {
            _uInt16Indexer[address] = value.Offset;
            _uInt16Indexer[address + 2] = value.Segment;
        }
    }
    
    public (ushort Segment, ushort Offset) this[ushort segment, ushort offset] {
        get => this[MemoryUtils.ToPhysicalAddress(segment, offset)];
        set => this[MemoryUtils.ToPhysicalAddress(segment, offset)] = value;
    }
}