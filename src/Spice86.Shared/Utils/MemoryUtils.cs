namespace Spice86.Shared.Utils;
using Spice86.Shared.Emulator.Memory;


using System.ComponentModel.DataAnnotations;

/// <summary>
/// Utils to get and set values in an array. Words and DWords are considered to be stored
/// little-endian.
/// </summary>
public static class MemoryUtils {
    /// <summary>
    /// Converts a segment and an offset into a physical address.
    /// </summary>
    /// <param name="segment">The segment value.</param>
    /// <param name="offset">The offset value.</param>
    /// <returns>The physical address that corresponds to the specified segment and offset.</returns>
    public static uint ToPhysicalAddress(ushort segment, ushort offset) {
        return (uint)(segment << 4) + offset;
    }

    /// <summary>
    /// Converts a physical address to its corresponding segment.
    /// </summary>
    /// <param name="physicalAddress">The physical address to convert.</param>
    /// <returns>The segment of the physical address.</returns>
    public static ushort ToSegment(uint physicalAddress) {
        return (ushort)(physicalAddress >> 4);
    }

    /// <summary>
    /// Converts a physical address to a corresponding <see cref="SegmentedAddress"/>
    /// </summary>
    /// <param name="physicalAddress">The unsigned integer representing the physical memory address.</param>
    /// <remarks>The maximum is equal to <c>0x10FFEF</c>, which is the end of the High Memory Area.</remarks>
    /// <returns>A segmented address representation of the physical address.</returns>
    public static SegmentedAddress ToSegmentedAddress([Range(0, 0x10FFEF)] uint physicalAddress) {
        ushort segment;
        ushort offset;
        // Compute the “raw” segment (physicalAddress >> 4).
        // If that value is greater than 0xFFFF, use the maximum segment,
        // and let offset absorb the remainder.
        if ((physicalAddress >> 4) > 0xFFFF) {
            segment = 0xFFFF;
            offset = (ushort)(physicalAddress - (0xFFFFu << 4));
        } else {
            segment = ToSegment(physicalAddress);
            offset = (ushort)(physicalAddress & 0xF);
        }
        return new SegmentedAddress(segment, offset);
    }
}