namespace Spice86.Core.Emulator.CPU.Exceptions;

/// <summary>
/// A Segment Not Present fault (#NP, vector 11) is raised when a segment-load instruction (other than
/// loading SS, which raises <see cref="CpuStackSegmentFaultException"/> instead) references a
/// descriptor whose present bit is clear. The saved instruction pointer points to the instruction
/// that caused the exception.
/// </summary>
public class CpuSegmentNotPresentException : CpuException {
    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="message">The message describing the error.</param>
    /// <param name="errorCode">The selector-related error code, or 0 for a violation not tied to a specific selector (#NP always carries an error code on real hardware).</param>
    public CpuSegmentNotPresentException(string message, ushort? errorCode = 0)
        : base(message, 0x0B, CpuExceptionType.Fault, "#NP", errorCode) {
    }
}
