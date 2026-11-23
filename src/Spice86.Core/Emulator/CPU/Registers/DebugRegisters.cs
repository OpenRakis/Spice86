namespace Spice86.Core.Emulator.CPU.Registers;

/// <summary>
/// The 386 debug registers (DR0-DR7). Raw values are stored in full so that
/// <c>MOV DRn, r/m32</c> round-trips every bit, including the hardware-reserved bits this emulator
/// forces; the emulator does not raise debug exceptions, so the stored values are not acted upon.
/// </summary>
public class DebugRegisters {
    /// <summary>DR0: debug register 0 address.</summary>
    public uint Dr0 { get; set; }

    /// <summary>DR1: debug register 1 address.</summary>
    public uint Dr1 { get; set; }

    /// <summary>DR2: debug register 2 address.</summary>
    public uint Dr2 { get; set; }

    /// <summary>DR3: debug register 3 address.</summary>
    public uint Dr3 { get; set; }

    /// <summary>DR4: reserved on the 386; writes go to DR6.</summary>
    public uint Dr4 { get; set; }

    /// <summary>DR5: reserved on the 386; writes go to DR7.</summary>
    public uint Dr5 { get; set; }

    /// <summary>DR6: debug exception status.</summary>
    public uint Dr6 { get; set; }

    /// <summary>DR7: debug control.</summary>
    public uint Dr7 { get; set; }
}
