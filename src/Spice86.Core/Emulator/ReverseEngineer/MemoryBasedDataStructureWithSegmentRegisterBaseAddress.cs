namespace Spice86.Core.Emulator.ReverseEngineer;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.VM;

/// <summary>
/// Provides a base class for memory-based data structures that have a segmented base address. <br/>
/// That segmented address is stored in a CPU segment register.
/// </summary>
public class MemoryBasedDataStructureWithSegmentRegisterBaseAddress : MemoryBasedDataStructureWithBaseAddressProvider {
    private readonly int _segmentRegisterIndex;

    private readonly SegmentRegisters _segmentRegisters;
    
    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="machine">The emulator machine.</param>
    /// <param name="segmentRegisterIndex">The index of the CPU segment register that stores the segmented base address.</param>

    public MemoryBasedDataStructureWithSegmentRegisterBaseAddress(Machine machine, int segmentRegisterIndex) : base(machine.Memory) {
        _segmentRegisterIndex = segmentRegisterIndex;
        _segmentRegisters = machine.Cpu.State.SegmentRegisters;
    }

    /// <inheritdoc />
    public override uint BaseAddress => (uint)(_segmentRegisters.GetRegister16(_segmentRegisterIndex) * 0x10);
}