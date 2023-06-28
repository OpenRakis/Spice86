namespace Spice86.Shared.Emulator.Memory;

using Spice86.Shared.Utils;

using System;
using System.Text.Json.Serialization;

/// <summary>
/// An address that is represented with a real mode segment and an offset.
/// </summary>
public class SegmentedAddress : IComparable<SegmentedAddress> {
    /// <summary>
    /// Initializes a new instance of the SegmentedAddress class from another instance, creating a copy.
    /// </summary>
    /// <param name="other">The other object to initialize from.</param>
    public SegmentedAddress(SegmentedAddress other) : this(other.Segment, other.Offset) {
    }

    /// <summary>
    /// Initializes a new instance of the SegmentedAddress class from a ValueTuple which first value is the segment and second is the offset
    /// </summary>
    /// <param name="segmentOffset">First value is the segment and second is the offset</param>
    public SegmentedAddress(ValueTuple<ushort, ushort> segmentOffset) : this(segmentOffset.Item1, segmentOffset.Item2) {
    }

    /// <summary>
    /// Initializes a new instance of the SegmentedAddress class with the given segment and offset.
    /// </summary>
    /// <param name="segment">The segment value of the address.</param>
    /// <param name="offset">The offset value of the address.</param>
    [JsonConstructor]
    public SegmentedAddress(ushort segment, ushort offset) {
        Segment = segment;
        Offset = offset;
    }

    /// <summary>
    /// Compares the current SegmentedAddress object with another SegmentedAddress object.
    /// </summary>
    /// <param name="other">The SegmentedAddress to compare with the current SegmentedAddress.</param>
    /// <returns>A value that indicates the relative order of the objects being compared.</returns>
    public int CompareTo(SegmentedAddress? other) {
        if (other == null) {
            return 1;
        }
        uint x = ToPhysical();
        uint y = other.ToPhysical();
        return x < y ? -1 : x == y ? 0 : 1;
    }

    /// <summary>
    /// Determines whether the current SegmentedAddress object is equal to another SegmentedAddress object.
    /// </summary>
    /// <param name="obj">The object to compare with the current SegmentedAddress object.</param>
    /// <returns>true if the objects are equal; otherwise, false.</returns>
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
    /// Adds the offset to the current SegmentedAddress object and returns the result as a new SegmentedAddress object.
    /// </summary>
    /// <param name="x">The SegmentedAddress to add the offset to.</param>
    /// <param name="y">The value to add to the offset of the SegmentedAddress.</param>
    /// <returns>A new SegmentedAddress object that represents the result of the addition.</returns>
    public static SegmentedAddress operator +(SegmentedAddress x, ushort y) {
        x.Offset += y;
        return x;
    }

    /// <summary>
    /// Gets the hash code for the current SegmentedAddress object.
    /// </summary>
    /// <returns>A 32-bit signed integer hash code.</returns>
    public override int GetHashCode() {
        return (int)ToPhysical();
    }
    
    /// <summary>
    /// Gets or sets the offset value of the address.
    /// </summary>
    public ushort Offset { get; set; }

    /// <summary>
    /// Gets or sets the segment value of the address.
    /// </summary>
    public ushort Segment { get; set; }
    
    /// <summary>
    /// Converts the SegmentedAddress object to a 32-bit physical address.
    /// </summary>
    /// <returns>A 32-bit physical address.</returns>
    public uint ToPhysical() {
        return MemoryUtils.ToPhysicalAddress(Segment, Offset);
    }

    /// <summary>
    /// Returns a string representation of the SegmentedAddress object in the form of a segment and offset value.
    /// </summary>
    /// <returns>A string representation of the SegmentedAddress object in the form of a segment and offset value.</returns>
    public string ToSegmentOffsetRepresentation() {
        return ConvertUtils.ToSegmentedAddressRepresentation(Segment, Offset);
    }

    /// <summary>
    /// Converts the SegmentedAddress object to a string in segment:offset/hex-physical format.
    /// </summary>
    /// <returns>A string representation of the SegmentedAddress object.</returns>
    public override string ToString() {
        return $"{ToSegmentOffsetRepresentation()}/{ConvertUtils.ToHex(ToPhysical())}";
    }
    
    public static string ToString(SegmentedAddress? segmentedAddress) {
        if (segmentedAddress is not null) {
            return segmentedAddress.ToString();
        }

        return "null";
    }
}