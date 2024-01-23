namespace Spice86.Core.Emulator.ReverseEngineer.DataStructure;

using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.VM;

/// <summary>
/// Provides a base class for memory-based data structures that have a segmented base address. <br/>
/// That segmented address is stored in a CPU segment register.
/// </summary>
public class MemoryBasedDataStructureWithSegmentRegisterBaseAddress : AbstractMemoryBasedDataStructure {
    private readonly uint _segmentRegisterIndex;

    private readonly SegmentRegisters _segmentRegisters;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="machine">The emulator machine.</param>
    /// <param name="segmentRegisterIndex">The index of the CPU segment register that stores the segmented base address.</param>

    public MemoryBasedDataStructureWithSegmentRegisterBaseAddress(Machine machine, uint segmentRegisterIndex) : base(machine.Memory) {
        _segmentRegisterIndex = segmentRegisterIndex;
        _segmentRegisters = machine.CpuState.SegmentRegisters;
    }

    /// <inheritdoc />
    public override uint BaseAddress => (uint)(_segmentRegisters.UInt16[_segmentRegisterIndex] * 0x10);
}