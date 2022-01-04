namespace Spice86.Emulator.ReverseEngineer;

using Spice86.Emulator.Memory;

public abstract class MemoryBasedArray : MemoryBasedDataStructureWithBaseAddress
{
    private readonly int _length;

    protected MemoryBasedArray(Memory memory, int baseAddress, int length) : base(memory, baseAddress)
    {
        this._length = length;
    }

    public int GetLength()
    {
        return _length;
    }

    public abstract int GetValueAt(int index);

    public abstract int GetValueSize();

    public int IndexToOffset(int index)
    {
        return index * GetValueSize();
    }

    public abstract void SetValueAt(int index, int value);
}