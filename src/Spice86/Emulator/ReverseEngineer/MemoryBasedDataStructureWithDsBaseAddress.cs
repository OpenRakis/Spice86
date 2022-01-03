namespace Spice86.Emulator.ReverseEngineer;

using Spice86.Emulator.Cpu;
using Spice86.Emulator.Machine;

public class MemoryBasedDataStructureWithDsBaseAddress : MemoryBasedDataStructureWithSegmentRegisterBaseAddress
{
    public MemoryBasedDataStructureWithDsBaseAddress(Machine machine) : base(machine, SegmentRegisters.CS_INDEX)
    {

    }
}
