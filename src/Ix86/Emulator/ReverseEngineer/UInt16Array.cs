namespace Ix86.Emulator.ReverseEngineer
{
    using Ix86.Emulator.Memory;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    public class Uint16Array : MemoryBasedArray
    {
        public Uint16Array(Memory memory, int baseAddress, int length) : base(memory, baseAddress, length)
        {
        }

        public override int GetValueSize()
        {
            return 2;
        }

        public override int GetValueAt(int index)
        {
            int offset = this.IndexToOffset(index);
            return GetUint16(offset);
        }

        public override void SetValueAt(int index, int value)
        {
            int offset = this.IndexToOffset(index);
            SetUint16(offset, value);
        }
    }
}
