namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;

using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.ReverseEngineer;

/// <summary>
/// Represents the current EMS page mapped in main memory
/// </summary>
public class EmsMemoryMapper : MemoryBasedDataStructureWithBaseAddress
{
    public EmsMemoryMapper(Memory memory, uint baseAddress) : base(memory, baseAddress) {
    }
}
