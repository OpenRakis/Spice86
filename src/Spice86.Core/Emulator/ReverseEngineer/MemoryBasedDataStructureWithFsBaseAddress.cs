namespace Spice86.Core.Emulator.ReverseEngineer;

using Spice86.Core.Emulator.CPU;

using Spice86.Core.Emulator.VM;

public class MemoryBasedDataStructureWithFsBaseAddress : MemoryBasedDataStructureWithSegmentRegisterBaseAddress {
    public MemoryBasedDataStructureWithFsBaseAddress(Machine machine) : base(machine, SegmentRegisters.FsIndex) {
    }
}