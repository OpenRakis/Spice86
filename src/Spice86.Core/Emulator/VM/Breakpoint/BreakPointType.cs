namespace Spice86.Core.Emulator.VM.Breakpoint;

/// <summary>
/// Types of breakpoints available
/// </summary>
public enum BreakPointType {
    /// <summary>
    /// CPU breakpoint triggered when address of instruction to be executed matches the address
    /// specified in the breakpoint.
    /// </summary>
    CPU_EXECUTION_ADDRESS,
    
    /// <summary>
    /// CPU breakpoint triggered when an interrupt is executed.
    /// </summary>
    CPU_INTERRUPT,

    /// <summary>
    /// CPU breakpoint triggered when the number of cycles executed by the CPU reach the number
    /// specified in the breakpoint.
    /// </summary>
    CPU_CYCLES,

    /// <summary>
    /// Memory breakpoint triggered when memory is read at the address specified in the breakpoint.
    /// </summary>
    MEMORY_READ,

    /// <summary>
    /// Memory breakpoint triggered when memory is written at the address specified in the breakpoint.
    /// </summary>
    MEMORY_WRITE,

    /// <summary>
    /// Memory breakpoint triggered when memory is read or written at the address specified in the breakpoint.
    /// </summary>
    MEMORY_ACCESS,
    
    /// <summary>
    /// IO breakpoint triggered when a port is read at the address specified in the breakpoint.
    /// </summary>
    IO_READ,

    /// <summary>
    /// IO breakpoint triggered when a port is written at the address specified in the breakpoint.
    /// </summary>
    IO_WRITE,

    /// <summary>
    /// IO breakpoint triggered when a port is read or written at the address specified in the breakpoint.
    /// </summary>
    IO_ACCESS,

    /// <summary> Breakpoint is triggered when the machine stops. </summary>
    MACHINE_STOP
}