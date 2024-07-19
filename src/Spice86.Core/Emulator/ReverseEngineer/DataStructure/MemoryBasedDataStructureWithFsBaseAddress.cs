namespace Spice86.Core.Emulator.ReverseEngineer.DataStructure;

using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.VM;

/// <summary>
/// Represents a memory-based data structures that has a segmented base address. <br/>
/// That segmented address is stored in the FS register.
/// </summary>
public class MemoryBasedDataStructureWithFsBaseAddress : MemoryBasedDataStructureWithSegmentRegisterBaseAddress {
    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    /// <param name="segmentRegisters">The CPU segment registers.</param>
    public MemoryBasedDataStructureWithFsBaseAddress(IByteReaderWriter memory, SegmentRegisters segmentRegisters) : base(memory, segmentRegisters, (uint)SegmentRegisterIndex.FsIndex) {
    }
}