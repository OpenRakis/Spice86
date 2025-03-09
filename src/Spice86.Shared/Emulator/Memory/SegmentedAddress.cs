namespace Spice86.Shared.Emulator.Memory;

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json.Serialization;

/// <summary>
/// An address that is represented with a real mode segment and an offset.
/// </summary>
public readonly record struct SegmentedAddress : IComparable<SegmentedAddress> {
    /// <summary>
    /// Initializes a new instance of the SegmentedAddress class with the given segment and offset.
    /// </summary>
    /// <param name="segment">The segment value of the address.</param>
    /// <param name="offset">The offset value of the address.</param>
    [JsonConstructor]
    public SegmentedAddress(ushort segment, ushort offset) {
        Segment = segment;
        Offset = offset;
        Linear = (uint)(Segment << 4) + Offset;
    }

    /// <summary>
    /// Gets the offset value of the address.
    /// </summary>
    public ushort Offset { get; }

    /// <summary>
    /// Gets the segment value of the address.
    /// </summary>
    public ushort Segment { get; }

    /// <summary>
    /// Gets the linear value of the address.
    /// </summary>
    public uint Linear { get; }

    /// <summary>
    /// Compares the current SegmentedAddress object with another SegmentedAddress object.
    /// </summary>
    /// <param name="other">The SegmentedAddress to compare with the current SegmentedAddress.</param>
    /// <returns>A value that indicates the relative order of the objects being compared.</returns>
    public int CompareTo(SegmentedAddress other) {
        return this < other ? -1 : this == other ? 0 : 1;
    }

    /// <summary>
    /// Determines whether the current SegmentedAddress object is equivalent to another SegmentedAddress object.
    /// </summary>
    /// <param name="other">The object to compare with the current SegmentedAddress object.</param>
    /// <returns>true if the objects are equivalent; otherwise, false.</returns>
    public bool Equals(SegmentedAddress other) {
        return Linear == other.Linear;
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

        return this < other.Value ? -1 : this == other.Value ? 0 : 1;
    }

    /// <summary>
    /// Adds the offset to a new SegmentedAddress instance.
    /// </summary>
    /// <param name="address">The original SegmentedAddress.</param>
    /// <param name="value">The value to add to the offset of the SegmentedAddress.</param>
    /// <returns>A new SegmentedAddress instance that represents the result of the addition.</returns>
    public static SegmentedAddress operator +(SegmentedAddress address, ushort value) {
        int newOffset = address.Offset + value;
        if (newOffset <= ushort.MaxValue) {
            return new SegmentedAddress(address.Segment, (ushort)newOffset);
        }

        return new SegmentedAddress((ushort)(address.Segment + 0x1000), unchecked((ushort)newOffset));
    }

    /// <summary>
    /// Subtracts the offset from a SegmentedAddress instance.
    /// </summary>
    /// <param name="address">The original SegmentedAddress</param>
    /// <param name="value">The value to subtract from the offset of the SegmentedAddress</param>
    /// <returns>A new SegmentedAddress instance that represents the result of the subtraction</returns>
    public static SegmentedAddress operator -(SegmentedAddress address, ushort value) {
        int newOffset = address.Offset - value;
        if (newOffset >= 0) {
            return new SegmentedAddress(address.Segment, (ushort)newOffset);
        }

        return new SegmentedAddress((ushort)(address.Segment - 0x1000), unchecked((ushort)newOffset));
    }

    /// <summary>
    /// Compares the current SegmentedAddress object with another SegmentedAddress object.
    /// </summary>
    /// <param name="left"></param>
    /// <param name="right"></param>
    /// <returns>True if the current SegmentedAddress object is greater than or equal to the other SegmentedAddress object; otherwise, false.</returns>
    public static bool operator >=(SegmentedAddress left, SegmentedAddress right) {
        return left.Linear >= right.Linear;
    }

    /// <summary>
    /// Compares the current SegmentedAddress object with another SegmentedAddress object.
    /// </summary>
    /// <param name="left"></param>
    /// <param name="right"></param>
    /// <returns>True if the current SegmentedAddress object is less than or equal to the other SegmentedAddress object; otherwise, false.</returns>
    public static bool operator <=(SegmentedAddress left, SegmentedAddress right) {
        return left.Linear <= right.Linear;
    }

    /// <summary>
    /// Compares the current SegmentedAddress object with another SegmentedAddress object.
    /// </summary>
    /// <param name="left"></param>
    /// <param name="right"></param>
    /// <returns>True if the current SegmentedAddress object is greater than the other SegmentedAddress object; otherwise, false.</returns>
    public static bool operator >(SegmentedAddress left, SegmentedAddress right) {
        return left.Linear > right.Linear;
    }

    /// <summary>
    /// Compares the current SegmentedAddress object with another SegmentedAddress object.
    /// </summary>
    /// <param name="left"></param>
    /// <param name="right"></param>
    /// <returns>True if the current SegmentedAddress object is less than the other SegmentedAddress object; otherwise, false.</returns>
    public static bool operator <(SegmentedAddress left, SegmentedAddress right) {
        return left.Linear < right.Linear;
    }

    /// <summary>
    /// Implicitly converts a tuple of segment and offset to a SegmentedAddress object.
    /// </summary>
    /// <param name="segmentOffset">The tuple of segment and offset to convert.</param>
    /// <returns>A new SegmentedAddress object.</returns>
    public static implicit operator SegmentedAddress((ushort sgement, ushort offset) segmentOffset) {
        return new SegmentedAddress(segmentOffset.sgement, segmentOffset.offset);
    }

    /// <summary>
    /// Gets the hash code for the current SegmentedAddress object.
    /// </summary>
    /// <returns>A 32-bit signed integer hash code.</returns>
    public override int GetHashCode() {
        return (int)Linear;
    }

    /// <summary>
    /// Converts the SegmentedAddress object to a string in segment:offset/hex-physical format.
    /// </summary>
    /// <returns>A string representation of the SegmentedAddress object.</returns>
    public override string ToString() {
        return $"{Segment:X4}:{Offset:X4}";
    }

    /// <summary>
    /// Converts the SegmentedAddress object to a string in segment:offset/hex-physical format.
    /// </summary>
    /// <returns>A string representation of the SegmentedAddress object, or "null" if input was <c>null</c>.</returns>
    public static string ToString(SegmentedAddress? segmentedAddress) {
        if (segmentedAddress == null) {
            return "null";
        }

        return segmentedAddress.Value.ToString();
    }

    /// <summary>
    /// Deconstructs the SegmentedAddress into two ushort values, the Segment and Offset
    /// </summary>
    /// <param name="segment">The segment part of the segmented address.</param>
    /// <param name="offset">The offset part of the segmented address.</param>
    public void Deconstruct(out ushort segment, out ushort offset) {
        segment = Segment;
        offset = Offset;
    }

    /// <summary>
    /// Tries to parse a hexadecimal string in the format of segment:offset into a SegmentedAddress object.
    /// </summary>
    /// <param name="s">a hex string in the format of segment:offset</param>
    /// <param name="segmentedAddress"></param>
    /// <returns>true if s was converted successfully; otherwise, false.</returns>
    public static bool TryParse(string? s, [NotNullWhen(true)] out SegmentedAddress? segmentedAddress) {
        if (string.IsNullOrWhiteSpace(s)) {
            segmentedAddress = null;

            return false;
        }
        string[] split = s.Split(":");
        if (split.Length == 2
            && ushort.TryParse(split[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort segment)
            && ushort.TryParse(split[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort offset)) {
            segmentedAddress = new SegmentedAddress(segment, offset);

            return true;
        }

        segmentedAddress = null;

        return false;
    }
}