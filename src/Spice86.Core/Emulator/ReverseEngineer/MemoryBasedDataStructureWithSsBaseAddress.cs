namespace Spice86.Core.Emulator.ReverseEngineer;

using Spice86.Core.Emulator.CPU;

using Spice86.Core.Emulator.VM;

public class MemoryBasedDataStructureWithSsBaseAddress : MemoryBasedDataStructureWithSegmentRegisterBaseAddress {
    public MemoryBasedDataStructureWithSsBaseAddress(Machine machine) : base(machine, SegmentRegisters.SsIndex) {
    }
}