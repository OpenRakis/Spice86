namespace Spice86.Core.Emulator.CPU.Exceptions;

/// <summary>
/// The Division Error occurs when dividing any number by 0 using the DIV or IDIV instruction, or when the division
/// result is too large to be represented in the destination. Since a faulting DIV or IDIV instruction is very easy to
/// insert anywhere in the code, many OS developers use this exception to test whether their exception handling code
/// works.
/// The saved instruction pointer points to the DIV or IDIV instruction which caused the exception. 
/// </summary>
public class CpuDivisionErrorException : CpuException {
    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="message">The message describing the exception.</param>
    public CpuDivisionErrorException(string message)
        : base(message, 0x00, CpuExceptionType.Fault, "#DE") {
    }
}