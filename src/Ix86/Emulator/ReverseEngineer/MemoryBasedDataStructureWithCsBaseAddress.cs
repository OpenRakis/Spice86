namespace Ix86.Emulator.ReverseEngineer;

using Ix86.Emulator.Cpu;
using Ix86.Emulator.Machine;

public class MemoryBasedDataStructureWithCsBaseAddress : MemoryBasedDataStructureWithSegmentRegisterBaseAddress
{
    public MemoryBasedDataStructureWithCsBaseAddress(Machine machine) : base(machine, SegmentRegisters.CS_INDEX)
    {
        
    }
}