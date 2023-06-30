namespace Spice86.Core.Emulator.ReverseEngineer;

using Spice86.Core.Emulator.Memory;

/// <summary>
/// Base class for all classes that represent a memory based data structure.
/// </summary>
public class MemoryBasedDataStructure {
    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="memory">The memory bus</param>
    public MemoryBasedDataStructure(IMemory memory) {
        Memory = memory;
    }

    /// <summary>
    /// The memory bus.
    /// </summary>
    public IMemory Memory { get; private set; }

    /// <summary>
    /// Reads a 2-byte value from RAM.
    /// </summary>
    /// <param name="baseAddress">The base address of the structure.</param>
    /// <param name="offset">The offset added to the address.</param>
    public ushort GetUint16(uint baseAddress, int offset) {
        return Memory.GetUint16((uint)(baseAddress + offset));
    }

    /// <summary>
    /// Reads an array of 2-byte values from RAM.
    /// </summary>
    /// <param name="baseAddress">The base address of the structure.</param>
    /// <param name="start">The offset added to the address.</param>
    /// <param name="length">The length of the array.</param>
    public Uint16Array GetUint16Array(uint baseAddress, int start, int length) {
        return new Uint16Array(Memory, (uint)(baseAddress + start), length);
    }

    /// <summary>
    /// Reads a 4-byte value from RAM.
    /// </summary>
    /// <param name="baseAddress">The base address of the structure.</param>
    /// <param name="offset">The offset added to the address.</param>
    public uint GetUint32(uint baseAddress, int offset) {
        return Memory.GetUint32((uint)(baseAddress + offset));
    }

    /// <summary>
    /// Reads a 1-byte value from RAM.
    /// </summary>
    /// <param name="baseAddress">The base address of the structure.</param>
    /// <param name="offset">The offset added to the address.</param>
    public byte GetUint8(uint baseAddress, int offset) {
        return Memory.GetUint8((uint)(baseAddress + offset));
    }

    /// <summary>
    /// Reads an array of 1-byte values from RAM.
    /// </summary>
    /// <param name="baseAddress">The base address of the structure.</param>
    /// <param name="start">The offset added to the address.</param>
    /// <param name="length">The length of the array.</param>
    public Uint8Array GetUint8Array(uint baseAddress, int start, int length) {
        return new Uint8Array(Memory, (uint)(baseAddress + start), length);
    }

    /// <summary>
    /// Writes a 2-byte value to RAM.
    /// </summary>
    /// <param name="baseAddress">The base address of the structure.</param>
    /// <param name="offset">The offset added to the address.</param>
    /// <param name="value">The value to write</param>
    public void SetUint16(uint baseAddress, int offset, ushort value) {
        Memory.SetUint16((uint)(baseAddress + offset), value);
    }

    /// <summary>
    /// Writes a 4-byte value to RAM.
    /// </summary>
    /// <param name="baseAddress">The base address of the structure.</param>
    /// <param name="offset">The offset added to the address.</param>
    /// <param name="value">The value to write</param>
    public void SetUint32(uint baseAddress, int offset, uint value) {
        Memory.SetUint32((uint)(baseAddress + offset), value);
    }

    /// <summary>
    /// Writes a 1-byte value to RAM.
    /// </summary>
    /// <param name="baseAddress">The base address of the structure.</param>
    /// <param name="offset">The offset added to the address.</param>
    /// <param name="value">The value to write</param>
    public void SetUint8(uint baseAddress, int offset, byte value) {
        Memory.SetUint8((uint)(baseAddress + offset), value);
    }
}