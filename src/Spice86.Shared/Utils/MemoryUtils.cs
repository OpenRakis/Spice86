namespace Spice86.Shared.Utils;
using Spice86.Shared.Emulator.Memory;


using System.ComponentModel.DataAnnotations;
using System.Runtime.CompilerServices;

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
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ToPhysicalAddress(ushort segment, ushort offset) {
        return (uint)(segment << 4) + offset;
    }

    /// <summary>
    /// Converts a physical address to its corresponding segment.
    /// </summary>
    /// <param name="physicalAddress">The physical address to convert.</param>
    /// <returns>The segment corresponding to the physical address.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ushort ToSegment(uint physicalAddress) {
        return (ushort)(physicalAddress >> 4);
    }

    /// <summary>
    /// Extracts the high 16-bit word from a 32-bit address.
    /// </summary>
    /// <param name="address">The 32-bit address.</param>
    /// <returns>The high 16 bits of the address.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ushort GetHighWord(uint address) {
        return (ushort)(address >> 16);
    }

    /// <summary>
    /// Extracts the low 16-bit word from a 32-bit address.
    /// </summary>
    /// <param name="address">The 32-bit address.</param>
    /// <returns>The low 16 bits of the address.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ushort GetLowWord(uint address) {
        return (ushort)(address & 0xFFFF);
    }

    /// <summary>
    /// Combines high and low 16-bit words into a single 32-bit address.
    /// </summary>
    /// <param name="highWord">The high 16 bits.</param>
    /// <param name="lowWord">The low 16 bits.</param>
    /// <returns>The combined 32-bit address.</returns>
    public static uint To32BitAddress(ushort highWord, ushort lowWord) {
        return ((uint)highWord << 16) | lowWord;
    }
}