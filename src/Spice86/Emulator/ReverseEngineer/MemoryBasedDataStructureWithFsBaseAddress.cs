namespace Spice86.Emulator.ReverseEngineer;

using Spice86.Emulator.Cpu;
using Spice86.Emulator.Machine;

internal class MemoryBasedDataStructureWithFsBaseAddress : MemoryBasedDataStructureWithSegmentRegisterBaseAddress
{
    public MemoryBasedDataStructureWithFsBaseAddress(Machine machine) : base(machine, SegmentRegisters.CS_INDEX)
    {

    }
}