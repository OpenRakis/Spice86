namespace Ix86.Emulator.ReverseEngineer;

using Ix86.Emulator.Memory;

public abstract class MemoryBasedArray : MemoryBasedDataStructureWithBaseAddress
{
    private readonly int _length;
    protected MemoryBasedArray(Memory memory, int baseAddress, int length) : base(memory, baseAddress)
    {
        this._length = length;
    }

    public abstract int GetValueSize();
    public abstract int GetValueAt(int index);
    public abstract void SetValueAt(int index, int value);
    public virtual int IndexToOffset(int index)
    {
        return index * GetValueSize();
    }

    public virtual int GetLength()
    {
        return _length;
    }
}
