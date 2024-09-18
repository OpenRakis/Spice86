namespace Spice86.Core.Emulator.CPU;

using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure.Array;

/// <summary>
/// Wraps reads and writes to the Interrupt Vector Table (IVT)
/// </summary>
public class InterruptVectorTable : SegmentedAddressArray {
    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    public InterruptVectorTable(IByteReaderWriter memory) : base(memory, 0, 0x100) {
    }
}