namespace Spice86.Core.Emulator.CPU.Exceptions;

/// <summary>
/// A Stack Segment Fault (#SS, vector 12) is raised when a stack-relative memory operand
/// references an address outside the stack segment limit, when popping past the stack
/// segment limit, or for other stack-segment access errors. The saved instruction pointer
/// points to the instruction that caused the exception.
/// </summary>
public class CpuStackSegmentFaultException : CpuException {
    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="message">The message describing the error.</param>
    /// <param name="errorCode">Some exceptions may have an error code pushed on the stack.</param>
    public CpuStackSegmentFaultException(string message, ushort? errorCode = null)
        : base(message, 0x0C, CpuExceptionType.Fault, "#SS", errorCode) {
    }
}
