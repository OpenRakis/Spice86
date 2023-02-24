namespace Spice86.Core.Emulator.Errors;

public class InvalidOpcodeException : CpuException {
    public InvalidOpcodeException(string message)
        : base(message, 0x06, CpuExceptionType.Fault, "#UD") {
    }
}