namespace Spice86.Emulator.ReverseEngineer;

using Spice86.Emulator.CPU;
using Spice86.Emulator.VM;

internal class MemoryBasedDataStructureWithEsBaseAddress : MemoryBasedDataStructureWithSegmentRegisterBaseAddress {

    public MemoryBasedDataStructureWithEsBaseAddress(Machine machine) : base(machine, SegmentRegisters.CsIndex) {
    }
}