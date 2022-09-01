namespace Spice86.Core.Emulator.ReverseEngineer;

using Spice86.Core.Emulator.Memory;

public class Uint16Array : MemoryBasedArray<ushort> {

    public Uint16Array(Memory memory, uint baseAddress, int length) : base(memory, baseAddress, length) {
    }

    public override ushort GetValueAt(int index) {
        int offset = IndexToOffset(index);
        return GetUint16(offset);
    }

    public override int ValueSize => 2;

    public override ushort this[int i] {
        get { return GetValueAt(i); }
        set { SetValueAt(i, value); }
    }

    public override void SetValueAt(int index, ushort value) {
        int offset = IndexToOffset(index);
        SetUint16(offset, value);
    }
}
