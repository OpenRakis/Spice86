namespace Spice86.Emulator.Machine.Breakpoint;

public enum BreakPointType
{
    /// <summary>
    /// CPU breakpoint triggered when address of instruction to be executed matches the address
    /// specified in the breakpoint.
    /// </summary>
    EXECUTION,

    /// <summary>
    /// CPU breakpoint triggered when the number of cycles executed by the CPU reach the number
    /// specified in the breakpoint address.
    /// </summary>
    CYCLES,

    /// <summary>
    /// Memory breakpoint triggered when memory is read at the address specified in the breakpoint.
    /// </summary>
    READ,

    /// <summary>
    /// Memory breakpoint triggered when memory is written at the address specified in the breakpoint.
    /// </summary>
    WRITE,

    /// <summary>
    /// Memory breakpoint triggered when memory is read or written at the address specified in the breakpoint.
    /// </summary>
    ACCESS,

    /// <summary> Breakpoint is triggered when the machine stops. </summary>
    MACHINE_STOP
}