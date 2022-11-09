namespace Spice86.Core.Emulator.ReverseEngineer;

using Spice86.Core.Emulator.CPU;

using Spice86.Core.Emulator.VM;

public class MemoryBasedDataStructureWithCsBaseAddress : MemoryBasedDataStructureWithSegmentRegisterBaseAddress {
    public MemoryBasedDataStructureWithCsBaseAddress(Machine machine) : base(machine, SegmentRegisters.CsIndex) {
    }
}