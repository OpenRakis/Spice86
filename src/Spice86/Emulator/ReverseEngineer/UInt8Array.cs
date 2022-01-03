namespace Spice86.Emulator.ReverseEngineer;

using Spice86.Emulator.Memory;

public class Uint8Array : MemoryBasedArray
{
    public Uint8Array(Memory memory, int baseAddress, int length) : base(memory, baseAddress, length)
    {
    }

    public override int GetValueSize()
    {
        return 1;
    }

    public override int GetValueAt(int index)
    {
        int offset = this.IndexToOffset(index);
        return GetUint8(offset);
    }

    public override void SetValueAt(int index, int value)
    {
        int offset = this.IndexToOffset(index);
        SetUint8(offset, value);
    }
}
