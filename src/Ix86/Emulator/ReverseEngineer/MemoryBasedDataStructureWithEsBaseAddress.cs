namespace Ix86.Emulator.ReverseEngineer;

using Ix86.Emulator.Cpu;
using Ix86.Emulator.Machine;


internal class MemoryBasedDataStructureWithEsBaseAddress : MemoryBasedDataStructureWithSegmentRegisterBaseAddress
{
    public MemoryBasedDataStructureWithEsBaseAddress(Machine machine) : base(machine, SegmentRegisters.CS_INDEX)
    {

    }
}