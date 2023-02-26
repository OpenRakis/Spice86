namespace Spice86.Core.Emulator.CPU.Exceptions;

/// <summary>
/// Exceptions are classified as:
///    Faults: These can be corrected and the program may continue as if nothing happened.
///    Traps: Traps are reported immediately after the execution of the trapping instruction.
///    Aborts: Some severe unrecoverable error. 
/// </summary>
[Flags]
public enum CpuExceptionType {
    Fault = 1,
    Trap = 2,
    Abort = 4
}