namespace Ix86.Emulator.ReverseEngineer;

using Ix86.Emulator.Memory;

public class MemoryBasedDataStructure
{
    private readonly Memory _memory;
    public MemoryBasedDataStructure(Memory memory)
    {
        this._memory = memory;
    }

    public virtual Memory GetMemory()
    {
        return _memory;
    }

    public virtual int GetUint8(int baseAddress, int offset)
    {
        return _memory.GetUint8(baseAddress + offset);
    }

    public virtual void SetUint8(int baseAddress, int offset, int value)
    {
        _memory.SetUint8(baseAddress + offset, value);
    }

    public virtual int GetUint16(int baseAddress, int offset)
    {
        return _memory.GetUint16(baseAddress + offset);
    }

    public virtual void SetUint16(int baseAddress, int offset, int value)
    {
        _memory.SetUint16(baseAddress + offset, value);
    }

    public virtual int GetUint32(int baseAddress, int offset)
    {
        return _memory.GetUint32(baseAddress + offset);
    }

    public virtual void SetUint32(int baseAddress, int offset, int value)
    {
        _memory.SetUint32(baseAddress + offset, value);
    }

    public virtual Uint8Array GetUint8Array(int baseAddress, int start, int length)
    {
        return new Uint8Array(_memory, baseAddress + start, length);
    }

    public virtual Uint16Array GetUint16Array(int baseAddress, int start, int length)
    {
        return new Uint16Array(_memory, baseAddress + start, length);
    }
}
