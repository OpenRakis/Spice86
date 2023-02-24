namespace Spice86.Core.Emulator.Errors;

[Flags]
public enum CpuExceptionType {
    Fault = 1,
    Trap = 2,
    Abort = 4
}