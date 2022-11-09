namespace Spice86.Core.Emulator.ReverseEngineer;

using Spice86.Core.Emulator.CPU;

using Spice86.Core.Emulator.VM;

public class MemoryBasedDataStructureWithDsBaseAddress : MemoryBasedDataStructureWithSegmentRegisterBaseAddress {
    public MemoryBasedDataStructureWithDsBaseAddress(Machine machine) : base(machine, SegmentRegisters.DsIndex) {
    }
}