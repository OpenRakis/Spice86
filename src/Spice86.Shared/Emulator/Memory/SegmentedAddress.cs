namespace Spice86.Shared;

using Spice86.Shared.Utils;

using System;

/// <summary> An address that is represented with a real mode segment and an offset. </summary>
public class SegmentedAddress : IComparable<SegmentedAddress> {
    public SegmentedAddress(ushort segment, ushort offset) {
        Segment = segment;
        Offset = offset;
    }

    public int CompareTo(SegmentedAddress? other) {
        if (other == null) {
            return 1;
        }
        uint x = ToPhysical();
        uint y = other.ToPhysical();
        return x < y ? -1 : x == y ? 0 : 1;
    }

    public override bool Equals(object? obj) {
        if (this == obj) {
            return true;
        }
        if (obj is not SegmentedAddress other) {
            return false;
        }
        return MemoryUtils.ToPhysicalAddress(Segment, Offset) == MemoryUtils.ToPhysicalAddress(other.Segment, other.Offset);
    }

    /// <summary>
    /// Overloaded addition operator.
    /// </summary>
    public static SegmentedAddress operator +(SegmentedAddress x, ushort y) {
        x.Offset += y;
        return x;
    }

    public override int GetHashCode() {
        return (int)ToPhysical();
    }

    public ushort Offset { get; set; }

    public ushort Segment { get; set; }

    public uint ToPhysical() {
        return MemoryUtils.ToPhysicalAddress(Segment, Offset);
    }

    public string ToSegmentOffsetRepresentation() {
        return ConvertUtils.ToSegmentedAddressRepresentation(Segment, Offset);
    }

    public override string ToString() {
        return $"{ToSegmentOffsetRepresentation()}/{ConvertUtils.ToHex(ToPhysical())}";
    }
}