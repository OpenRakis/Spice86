namespace Spice86.Core.Emulator.CPU;

using Spice86.Core.Emulator.Memory;

/// <summary>
/// Wraps reads and writes to the Interrupt Vector Table (IVT)
/// </summary>
public class InterruptVectorTable {
    private readonly Memory _memory;

    public InterruptVectorTable(Memory memory) {
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
            return _memory.OffsetSegment[offsetAddress];
        }
        set {
            // install the vector in the vector table. Table starts at memory address 0.
            uint offsetAddress = (uint)(4 * vectorNumber);
            _memory.OffsetSegment[offsetAddress] = (value.Segment, value.Offset);
        }
    }
}