namespace Spice86.Core.Emulator.ReverseEngineer;

using Spice86.Core.Emulator.Memory;

/// <summary>
/// Provides a base class for memory-based data structures that have a base address.
/// </summary>
public abstract class MemoryBasedDataStructureWithBaseAddressProvider : MemoryBasedDataStructure {
    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    protected MemoryBasedDataStructureWithBaseAddressProvider(IMemoryStore memory) : base(memory) {
    }

    /// <summary>
    /// The base address of the data structure.
    /// </summary>
    public abstract uint BaseAddress { get; }

    /// <summary>
    /// Gets a 16-bit unsigned integer from the data structure at the specified offset.
    /// </summary>
    /// <param name="offset">The offset from the base address to read the value from.</param>
    /// <returns>The value at the specified offset.</returns>
    public ushort GetUint16(int offset) {
        return GetUint16(BaseAddress, offset);
    }

    /// <summary>
    /// Gets a uint16 array from the data structure starting at the specified offset and with the specified length.
    /// </summary>
    /// <param name="start">The offset from the base address to start reading values from.</param>
    /// <param name="length">The length of the array.</param>
    /// <returns>The array of uint16 values.</returns>
    public Uint16Array GetUint16Array(int start, int length) {
        return GetUint16Array(BaseAddress, start, length);
    }

    /// <summary>
    /// Gets a 32-bit unsigned integer from the data structure at the specified offset.
    /// </summary>
    /// <param name="offset">The offset from the base address to read the value from.</param>
    /// <returns>The value at the specified offset.</returns>
    public uint GetUint32(int offset) {
        return GetUint32(BaseAddress, offset);
    }

    /// <summary>
    /// Gets an 8-bit unsigned integer from the data structure at the specified offset.
    /// </summary>
    /// <param name="offset">The offset from the base address to read the value from.</param>
    /// <returns>The value at the specified offset.</returns>
    public byte GetUint8(int offset) {
        return GetUint8(BaseAddress, offset);
    }

    /// <summary>
    /// Gets an 8-bit unsigned integer array from the data structure starting at the specified offset and with the specified length.
    /// </summary>
    /// <param name="start">The offset from the base address to start reading values from.</param>
    /// <param name="length">The length of the array.</param>
    /// <returns>The array of uint8 values.</returns>
    public Uint8Array GetUint8Array(int start, int length) {
        return GetUint8Array(BaseAddress, start, length);
    }

    /// <summary>
    /// Gets a zero-terminated string from the data structure starting at the specified offset and with the specified maximum length.
    /// </summary>
    /// <param name="start">The offset from the base address to start reading characters from.</param>
    /// <param name="maxLength">The maximum length of the string.</param>
    /// <returns>The zero-terminated string.</returns>
    public string GetZeroTerminatedString(int start, int maxLength) {
        return Memory.GetZeroTerminatedString((uint)(BaseAddress + start), maxLength);
    }
    /// <summary>
    /// Sets a zero-terminated string in memory at a specified offset from the base address.
    /// </summary>
    /// <param name="start">The offset from the base address at which to start writing the string.</param>
    /// <param name="value">The string to write.</param>
    /// <param name="maxLength">The maximum length of the string.</param>
    public void SetZeroTerminatedString(int start, string value, int maxLength) {
        Memory.SetZeroTerminatedString((uint)(BaseAddress + start), value, maxLength);
    }

    /// <summary>
    /// Sets a 16-bit unsigned integer value in memory at a specified offset from the base address.
    /// </summary>
    /// <param name="offset">The offset from the base address at which to write the value.</param>
    /// <param name="value">The value to write.</param>
    public void SetUint16(int offset, ushort value) {
        SetUint16(BaseAddress, offset, value);
    }

    /// <summary>
    /// Sets a 32-bit unsigned integer value in memory at a specified offset from the base address.
    /// </summary>
    /// <param name="offset">The offset from the base address at which to write the value.</param>
    /// <param name="value">The value to write.</param>
    public void SetUint32(int offset, uint value) {
        SetUint32(BaseAddress, offset, value);
    }

    /// <summary>
    /// Sets an 8-bit unsigned integer value in memory at a specified offset from the base address.
    /// </summary>
    /// <param name="offset">The offset from the base address at which to write the value.</param>
    /// <param name="value">The value to write.</param>
    public void SetUint8(int offset, byte value) {
        SetUint8(BaseAddress, offset, value);
    }
}