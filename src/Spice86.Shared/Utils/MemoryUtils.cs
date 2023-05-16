namespace Spice86.Shared.Utils;

/// <summary>
/// Utils to get and set values in an array. Words and DWords are considered to be stored
/// little-endian.
/// </summary>
public static class MemoryUtils {
    /// <summary>
    /// Gets a 16-bit unsigned integer from the specified address in the memory array.
    /// </summary>
    /// <param name="memory">The memory array.</param>
    /// <param name="address">The address of the first byte of the 16-bit value.</param>
    /// <returns>The 16-bit unsigned integer stored at the specified address.</returns>
    public static ushort GetUint16(byte[] memory, uint address) {
        return (ushort)(memory[address] | memory[address + 1] << 8);
    }

    /// <summary>
    /// Gets a 32-bit unsigned integer from the specified address in the memory array.
    /// </summary>
    /// <param name="memory">The memory array.</param>
    /// <param name="address">The address of the first byte of the 32-bit value.</param>
    /// <returns>The 32-bit unsigned integer stored at the specified address.</returns>
    public static uint GetUint32(byte[] memory, uint address) {
        return (uint)(memory[address] | memory[address + 1] << 8 | memory[address + 2] << 16 | memory[address + 3] << 24);
    }

    /// <summary>
    /// Gets an 8-bit unsigned integer from the specified address in the memory array.
    /// </summary>
    /// <param name="memory">The memory array.</param>
    /// <param name="address">The address of the byte to retrieve.</param>
    /// <returns>The 8-bit unsigned integer stored at the specified address.</returns>
    public static byte GetUint8(byte[] memory, uint address) {
        return memory[address];
    }

    /// <summary>
    /// Sets a 16-bit unsigned integer at the specified address in the memory array.
    /// </summary>
    /// <param name="memory">The memory array.</param>
    /// <param name="address">The address of the first byte of the 16-bit value.</param>
    /// <param name="value">The 16-bit unsigned integer to store at the specified address.</param>
    public static void SetUint16(byte[] memory, uint address, ushort value) {
        memory[address] = (byte)value;
        memory[address + 1] = (byte)(value >> 8);
    }

    /// <summary>
    /// Sets a 32-bit unsigned integer at the specified address in the memory array.
    /// </summary>
    /// <param name="memory">The memory array.</param>
    /// <param name="address">The address of the first byte of the 32-bit value.</param>
    /// <param name="value">The 32-bit unsigned integer to store at the specified address.</param>
    public static void SetUint32(byte[] memory, uint address, uint value) {
        memory[address] = (byte)value;
        memory[address + 1] = (byte)(value >> 8);
        memory[address + 2] = (byte)(value >> 16);
        memory[address + 3] = (byte)(value >> 24);
    }

    /// <summary>
    /// Sets an 8-bit unsigned integer at the specified address in the memory array.
    /// </summary>
    /// <param name="memory">The memory array.</param>
    /// <param name="address">The address of the byte to store.</param>
    /// <param name="value">The 8-bit unsigned integer to store at the specified address.</param>
    public static void SetUint8(byte[] memory, uint address, byte value) {
        memory[address] = value;
    }

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