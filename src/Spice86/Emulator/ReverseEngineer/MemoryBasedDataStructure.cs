namespace Spice86.Emulator.ReverseEngineer;

using Spice86.Emulator.Memory;

public class MemoryBasedDataStructure {
    private readonly Memory _memory;

    public MemoryBasedDataStructure(Memory memory) {
        this._memory = memory;
    }

    public Memory GetMemory() {
        return _memory;
    }

    public ushort GetUint16(uint baseAddress, int offset) {
        return _memory.GetUint16((uint)(baseAddress + offset));
    }

    public Uint16Array GetUint16Array(uint baseAddress, int start, int length) {
        return new Uint16Array(_memory, (uint)(baseAddress + start), length);
    }

    public uint GetUint32(uint baseAddress, int offset) {
        return _memory.GetUint32((uint)(baseAddress + offset));
    }

    public byte GetUint8(uint baseAddress, int offset) {
        return _memory.GetUint8((uint)(baseAddress + offset));
    }

    public Uint8Array GetUint8Array(uint baseAddress, int start, int length) {
        return new Uint8Array(_memory, (uint)(baseAddress + start), length);
    }

    public void SetUint16(uint baseAddress, int offset, ushort value) {
        _memory.SetUint16((uint)(baseAddress + offset), value);
    }

    public void SetUint32(uint baseAddress, int offset, uint value) {
        _memory.SetUint32((uint)(baseAddress + offset), value);
    }

    public void SetUint8(uint baseAddress, int offset, byte value) {
        _memory.SetUint8((uint)(baseAddress + offset), value);
    }
}