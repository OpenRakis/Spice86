namespace Spice86.Core.Emulator.ReverseEngineer;

using Spice86.Core.Emulator.Memory;

public abstract class MemoryBasedArray<T> : MemoryBasedDataStructureWithBaseAddress {
    private readonly int _length;

    protected MemoryBasedArray(Memory memory, uint baseAddress, int length) : base(memory, baseAddress) {
        _length = length;
    }

    public abstract T this[int i] {
        get;
        set;
    }

    public int Length => _length;

    public abstract T GetValueAt(int index);

    public abstract int ValueSize { get; }

    public int IndexToOffset(int index) {
        return index * ValueSize;
    }

    public abstract void SetValueAt(int index, T value);
}