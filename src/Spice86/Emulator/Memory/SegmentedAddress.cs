namespace Spice86.Emulator.Memory;

using Spice86.Utils;
using System;

/// <summary> An address that is represented with a real mode segment and an offset. </summary>
public class SegmentedAddress : IComparable<SegmentedAddress> {
    private readonly int _offset;

    private readonly int _segment;

    public SegmentedAddress(int segment, int offset) {
        _segment = segment;
        _offset = offset;
    }

    public int CompareTo(SegmentedAddress? other) {
        int x = this.ToPhysical();
        int? y = other?.ToPhysical();
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
        return ToPhysical();
    }

    public int GetOffset() {
        return _offset;
    }

    public int GetSegment() {
        return _segment;
    }

    public int ToPhysical() {
        return MemoryUtils.ToPhysicalAddress(_segment, _offset);
    }

    public string ToSegmentOffsetRepresentation() {
        return ConvertUtils.ToSegmentedAddressRepresentation(_segment, _offset);
    }

    public override string ToString() {
        return $"{ToSegmentOffsetRepresentation()}/{ConvertUtils.ToHex(ToPhysical())}";
    }
}