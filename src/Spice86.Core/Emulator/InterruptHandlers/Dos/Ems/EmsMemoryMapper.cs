namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;

using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.ReverseEngineer;

public class EmsMemoryMapper : MemoryBasedDataStructureWithBaseAddress
{
    public EmsMemoryMapper(Memory memory, uint baseAddress) : base(memory, baseAddress) {
    }
}
