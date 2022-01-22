namespace Spice86.Emulator.ReverseEngineer {

    using Spice86.Emulator.Memory;

    public class Uint16Array : MemoryBasedArray<ushort> {

        public Uint16Array(Memory memory, uint baseAddress, int length) : base(memory, baseAddress, length) {
        }

        public override ushort GetValueAt(int index) {
            int offset = this.IndexToOffset(index);
            return GetUint16(offset);
        }

        public override int GetValueSize() {
            return 2;
        }

        public override void SetValueAt(int index, ushort value) {
            int offset = this.IndexToOffset(index);
            SetUint16(offset, (ushort)value);
        }
    }
}