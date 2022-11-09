namespace Spice86.Core.Emulator.ReverseEngineer;

using Spice86.Core.Emulator.CPU;

using Spice86.Core.Emulator.VM;

public class MemoryBasedDataStructureWithEsBaseAddress : MemoryBasedDataStructureWithSegmentRegisterBaseAddress {
    public MemoryBasedDataStructureWithEsBaseAddress(Machine machine) : base(machine, SegmentRegisters.EsIndex) {
    }
}