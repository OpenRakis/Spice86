namespace Spice86.Core.Emulator.CPU;

using Spice86.Core.Emulator.Memory.Indexable;

/// <summary>
/// Wraps reads and writes to the Interrupt Vector Table (IVT)
/// </summary>
public class InterruptVectorTable {
    private readonly IIndexable _memory;

    public InterruptVectorTable(IIndexable memory) {
        _memory = memory;
    }

    /// <summary>
    /// Reads or writes to the interrupt vector table
    /// </summary>
    /// <param name="vectorNumber">Vector to access in the interrupt table</param>
    public (ushort Segment, ushort Offset) this[byte vectorNumber] {
        get {
            // Table starts at memory address 0.
            uint offsetAddress = (uint)(4 * vectorNumber);
            return _memory.SegmentedAddressValue[offsetAddress];
        }
        set {
            // install the vector in the vector table. Table starts at memory address 0.
            uint offsetAddress = (uint)(4 * vectorNumber);
            _memory.SegmentedAddressValue[offsetAddress] = (value.Segment, value.Offset);
        }
    }
}