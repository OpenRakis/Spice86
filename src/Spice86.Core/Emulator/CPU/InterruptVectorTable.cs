namespace Spice86.Core.Emulator.CPU;

using Spice86.Core.Emulator.Memory.Indexable;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Wraps reads and writes to the Interrupt Vector Table (IVT)
/// </summary>
public class InterruptVectorTable {
    private readonly IIndexable _memory;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    public InterruptVectorTable(IIndexable memory) {
        _memory = memory;
    }

    /// <summary>
    /// Reads or writes to the interrupt vector table
    /// </summary>
    /// <param name="vectorNumber">Vector to access in the interrupt table</param>
    public SegmentedAddress this[byte vectorNumber] {
        get {
            // Table starts at memory address 0.
            uint offsetAddress = (uint)(4 * vectorNumber);
            return _memory.SegmentedAddress[offsetAddress];
        }
        set {
            // install the vector in the vector table. Table starts at memory address 0.
            uint offsetAddress = (uint)(4 * vectorNumber);
            _memory.SegmentedAddress[offsetAddress] = new(value.Segment, value.Offset);
        }
    }
}