namespace Spice86.Core.Emulator.CPU.Registers;

/// <summary>
/// A class that represents the segment registers of a CPU. <br/>
/// The segment registers are registers that store segment selectors, which are used to access different parts of memory.
/// </summary>
public class SegmentRegisters : RegistersHolder {
    public SegmentRegisters() : base(6) {
    }

}
