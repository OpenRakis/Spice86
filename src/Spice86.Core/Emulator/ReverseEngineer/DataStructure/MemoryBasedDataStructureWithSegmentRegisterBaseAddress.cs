namespace Spice86.Core.Emulator.ReverseEngineer.DataStructure;

using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.Memory.ReaderWriter;
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
    /// <param name="memory">The memory bus.</param>
    /// <param name="segmentRegisters">The CPU segment registers.</param>
    /// <param name="segmentRegisterIndex">The index of the CPU segment register that stores the segmented base address.</param>
    public MemoryBasedDataStructureWithSegmentRegisterBaseAddress(IByteReaderWriter memory, SegmentRegisters segmentRegisters, uint segmentRegisterIndex) : base(memory) {
        _segmentRegisterIndex = segmentRegisterIndex;
        _segmentRegisters = segmentRegisters;
    }

    /// <inheritdoc />
    public override uint BaseAddress => (uint)(_segmentRegisters.UInt16[_segmentRegisterIndex] * 0x10);
}