namespace Spice86.Emulator.Memory;

using Spice86.Utils;
using System;

/// <summary> An address that is represented with a real mode segment and an offset. </summary>
public class SegmentedAddress : IComparable<SegmentedAddress> {
    private readonly ushort _offset;

    private readonly ushort _segment;

    public SegmentedAddress(ushort segment, ushort offset) {
        _segment = segment;
        _offset = offset;
    }

    public int CompareTo(SegmentedAddress? other) {
        if (other == null) {
            return 1;
        }
        uint x = this.ToPhysical();
        uint y = other.ToPhysical();
        return (x < y) ? -1 : ((x == y) ? 0 : 1);
    }

    public override bool Equals(object? obj) {
        if (this == obj) {
            return true;
        }
        if (obj is not SegmentedAddress other) {
            return false;
        }
        return MemoryUtils.ToPhysicalAddress(_segment, _offset) == MemoryUtils.ToPhysicalAddress(other._segment, other._offset);
    }

    public override int GetHashCode() {
        return (int)ToPhysical();
    }

    public ushort GetOffset() {
        return _offset;
    }

    public ushort GetSegment() {
        return _segment;
    }

    public uint ToPhysical() {
        return MemoryUtils.ToPhysicalAddress(_segment, _offset);
    }

    public string ToSegmentOffsetRepresentation() {
        return ConvertUtils.ToSegmentedAddressRepresentation(_segment, _offset);
    }

    public override string ToString() {
        return $"{ToSegmentOffsetRepresentation()}/{ConvertUtils.ToHex(ToPhysical())}";
    }
}