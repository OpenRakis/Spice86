namespace Spice86.Core.Emulator.ReverseEngineer;

using Spice86.Core.Emulator.CPU;

using Spice86.Core.Emulator.VM;

public class MemoryBasedDataStructureWithGsBaseAddress : MemoryBasedDataStructureWithSegmentRegisterBaseAddress {

    public MemoryBasedDataStructureWithGsBaseAddress(Machine machine) : base(machine, SegmentRegisters.GsIndex) {
    }
}