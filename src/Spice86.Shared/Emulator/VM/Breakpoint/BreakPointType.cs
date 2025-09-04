namespace Spice86.Shared.Emulator.VM.Breakpoint;

/// <summary>
/// Types of breakpoints available
/// </summary>
/// <remarks>Entries are explicitly numbered, because this class is serialized as JSON when saving breakpoints.<br/>
/// New entries should use different numbers from old entries, and old entries should not change,<br/>
/// to ensure serialization compatibility over time.</remarks>
public enum BreakPointType {
    /// <summary>
    /// CPU breakpoint triggered when address of instruction to be executed matches the address
    /// specified in the breakpoint.
    /// </summary>
    CPU_EXECUTION_ADDRESS = 0,
    
    /// <summary>
    /// CPU breakpoint triggered when an interrupt is executed.
    /// </summary>
    CPU_INTERRUPT = 1,

    /// <summary>
    /// CPU breakpoint triggered when the number of cycles executed by the CPU reach the number
    /// specified in the breakpoint.
    /// </summary>
    CPU_CYCLES = 2,

    /// <summary>
    /// Memory breakpoint triggered when memory is read at the address specified in the breakpoint.
    /// </summary>
    MEMORY_READ = 3,

    /// <summary>
    /// Memory breakpoint triggered when memory is written at the address specified in the breakpoint.
    /// </summary>
    MEMORY_WRITE = 4,

    /// <summary>
    /// Memory breakpoint triggered when memory is read or written at the address specified in the breakpoint.
    /// </summary>
    MEMORY_ACCESS = 5,
    
    /// <summary>
    /// IO breakpoint triggered when a port is read at the address specified in the breakpoint.
    /// </summary>
    IO_READ = 6,

    /// <summary>
    /// IO breakpoint triggered when a port is written at the address specified in the breakpoint.
    /// </summary>
    IO_WRITE = 7,

    /// <summary>
    /// IO breakpoint triggered when a port is read or written at the address specified in the breakpoint.
    /// </summary>
    IO_ACCESS = 8,

    /// <summary> Breakpoint is triggered when the machine starts. </summary>
    MACHINE_START = 9,

    /// <summary> Breakpoint is triggered when the machine stops. </summary>
    MACHINE_STOP = 10
}