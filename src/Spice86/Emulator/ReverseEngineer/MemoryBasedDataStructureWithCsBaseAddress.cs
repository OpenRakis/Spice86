namespace Spice86.Emulator.ReverseEngineer;

using Spice86.Emulator.Cpu;
using Spice86.Emulator.Machine;

public class MemoryBasedDataStructureWithCsBaseAddress : MemoryBasedDataStructureWithSegmentRegisterBaseAddress
{
    public MemoryBasedDataStructureWithCsBaseAddress(Machine machine) : base(machine, SegmentRegisters.CsIndex)
    {
    }
}