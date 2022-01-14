namespace Spice86.Emulator.ReverseEngineer;

using Spice86.Emulator.Cpu;
using Spice86.Emulator.Machine;

internal class MemoryBasedDataStructureWithGsBaseAddress : MemoryBasedDataStructureWithSegmentRegisterBaseAddress {

    public MemoryBasedDataStructureWithGsBaseAddress(Machine machine) : base(machine, SegmentRegisters.CsIndex) {
    }
}