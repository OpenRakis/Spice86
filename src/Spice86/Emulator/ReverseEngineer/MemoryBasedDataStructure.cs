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

    public ushort GetUint16(int baseAddress, int offset) {
        return _memory.GetUint16(baseAddress + offset);
    }

    public Uint16Array GetUint16Array(int baseAddress, int start, int length) {
        return new Uint16Array(_memory, baseAddress + start, length);
    }

    public uint GetUint32(int baseAddress, int offset) {
        return _memory.GetUint32(baseAddress + offset);
    }

    public byte GetUint8(int baseAddress, int offset) {
        return _memory.GetUint8(baseAddress + offset);
    }

    public Uint8Array GetUint8Array(int baseAddress, int start, int length) {
        return new Uint8Array(_memory, baseAddress + start, length);
    }

    public void SetUint16(int baseAddress, int offset, ushort value) {
        _memory.SetUint16(baseAddress + offset, value);
    }

    public void SetUint32(int baseAddress, int offset, uint value) {
        _memory.SetUint32(baseAddress + offset, value);
    }

    public void SetUint8(int baseAddress, int offset, byte value) {
        _memory.SetUint8(baseAddress + offset, value);
    }
}