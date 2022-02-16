namespace Spice86.Emulator.ReverseEngineer;

using Spice86.Emulator.CPU;
using Spice86.Emulator.VM;

public class MemoryBasedDataStructureWithGsBaseAddress : MemoryBasedDataStructureWithSegmentRegisterBaseAddress {

    public MemoryBasedDataStructureWithGsBaseAddress(Machine machine) : base(machine, SegmentRegisters.CsIndex) {
    }
}