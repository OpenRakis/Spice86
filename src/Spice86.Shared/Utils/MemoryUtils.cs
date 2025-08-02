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
    /// <returns>The segment corresponding to the physical address.</returns>
    public static ushort ToSegment(uint physicalAddress) {
        return (ushort)(physicalAddress >> 4);
    }

    /// <summary>
    /// Converts a physical address to its offset, to be used in a segment:offset pair.
    /// </summary>
    /// <remarks>Ensure that you call <see cref="ToSegment(uint)"/> first in order to get a segmented address.</remarks>
    /// <param name="physicalAddress">The physical address to convert. Must be a 32-bit unsigned integer.</param>
    /// <returns>The offset portion of the segmented address as a 16-bit unsigned integer.</returns>
    public static ushort ToOffset(uint physicalAddress) {
        return (ushort)(physicalAddress & 0xffff);
    }
}