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
}