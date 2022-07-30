namespace Spice86.Core.Emulator.ReverseEngineer;

using Spice86.Core.Emulator.Memory;

public class Uint8Array : MemoryBasedArray<byte> {

    public Uint8Array(Memory memory, uint baseAddress, int length) : base(memory, baseAddress, length) {
    }

    public override byte GetValueAt(int index) {
        int offset = IndexToOffset(index);
        return GetUint8(offset);
    }

    public override int ValueSize => 1;

    public override byte this[int i] {
        get { return GetValueAt(i); }
        set { SetValueAt(i, value); }
    }

    public override void SetValueAt(int index, byte value) {
        int offset = IndexToOffset(index);
        SetUint8(offset, value);
    }
}