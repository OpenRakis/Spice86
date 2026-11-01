namespace Spice86.Core.Emulator.ReverseEngineer.DataStructure.Array;

using Spice86.Core.Emulator.Memory;

/// <summary>
/// A double-word table inside one 64KB segment: <see cref="this"/> reads and writes
/// <c>memory.UInt32[segment(), (ushort)(baseOffset + index * 4)]</c>, the segmented indexer path of
/// the code it replaces. <see cref="Length"/> documents the observed extent; it is not checked.
/// </summary>
public sealed class UInt32SegmentedTable {
    private readonly IMemory _memory;
    private readonly Func<ushort> _segment;
    private readonly ushort _baseOffset;

    /// <summary>Initializes a new instance of the <see cref="UInt32SegmentedTable"/> class.</summary>
    /// <param name="memory">Where data is read and written.</param>
    /// <param name="segment">The segment value at each access.</param>
    /// <param name="baseOffset">The offset of element 0 inside the segment.</param>
    /// <param name="length">The documented number of elements.</param>
    public UInt32SegmentedTable(IMemory memory, Func<ushort> segment, ushort baseOffset, int length) {
        _memory = memory;
        _segment = segment;
        _baseOffset = baseOffset;
        Length = length;
    }

    /// <summary>The documented number of elements; not enforced.</summary>
    public int Length { get; }

    /// <summary>Gets or sets element <paramref name="index"/>.</summary>
    public uint this[int index] {
        get => _memory.UInt32[_segment(), (ushort)(_baseOffset + index * 4)];
        set => _memory.UInt32[_segment(), (ushort)(_baseOffset + index * 4)] = value;
    }
}
