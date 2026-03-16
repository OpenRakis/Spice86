namespace Spice86.Core.Emulator.CPU.Exceptions;

/// <summary>
///     The Bound Range Exceeded exception (#BR) occurs when the BOUND instruction detects
///     that the tested index lies outside the supplied bounds.
/// </summary>
public class CpuBoundRangeExceededException : CpuException {
    /// <summary>
    ///     Initializes a new instance.
    /// </summary>
    /// <param name="message">The message describing the exception.</param>
    public CpuBoundRangeExceededException(string message)
        : base(message, 0x05, CpuExceptionType.Fault, "#BR") {
    }
}