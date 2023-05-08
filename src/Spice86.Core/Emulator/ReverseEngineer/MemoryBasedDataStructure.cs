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
    public MemoryBasedDataStructure(Memory memory) {
        Memory = memory;
    }

    /// <summary>
    /// The memory bus.
    /// </summary>
    public Memory Memory { get; private set; }

    public ushort GetUint16(uint baseAddress, int offset) {
        return Memory.GetUint16((uint)(baseAddress + offset));
    }

    public Uint16Array GetUint16Array(uint baseAddress, int start, int length) {
        return new Uint16Array(Memory, (uint)(baseAddress + start), length);
    }

    public uint GetUint32(uint baseAddress, int offset) {
        return Memory.GetUint32((uint)(baseAddress + offset));
    }

    public byte GetUint8(uint baseAddress, int offset) {
        return Memory.GetUint8((uint)(baseAddress + offset));
    }

    public Uint8Array GetUint8Array(uint baseAddress, int start, int length) {
        return new Uint8Array(Memory, (uint)(baseAddress + start), length);
    }

    public void SetUint16(uint baseAddress, int offset, ushort value) {
        Memory.SetUint16((uint)(baseAddress + offset), value);
    }

    public void SetUint32(uint baseAddress, int offset, uint value) {
        Memory.SetUint32((uint)(baseAddress + offset), value);
    }

    public void SetUint8(uint baseAddress, int offset, byte value) {
        Memory.SetUint8((uint)(baseAddress + offset), value);
    }
}