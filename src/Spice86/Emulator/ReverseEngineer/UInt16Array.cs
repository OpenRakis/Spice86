namespace Spice86.Emulator.ReverseEngineer {

    using Spice86.Emulator.Memory;

    public class Uint16Array : MemoryBasedArray {

        public Uint16Array(Memory memory, int baseAddress, int length) : base(memory, baseAddress, length) {
        }

        public override int GetValueAt(int index) {
            int offset = this.IndexToOffset(index);
            return GetUint16(offset);
        }

        public override int GetValueSize() {
            return 2;
        }

        public override void SetValueAt(int index, int value) {
            int offset = this.IndexToOffset(index);
            SetUint16(offset, (ushort)value);
        }
    }
}