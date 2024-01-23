namespace Spice86.Core.Emulator.CPU.Exceptions;

/// <summary>
/// A General Protection Fault may occur for various reasons. The most common are: <br/>
///   - Segment error (privilege, type, limit, read/write rights). <br/>
///   - Executing a privileged instruction while CPL != 0. <br/>
///   - Writing a 1 in a reserved register field or writing invalid value combinations (e.g. CR0 with PE=0 and PG=1). <br/>
///   - Referencing or accessing a null-descriptor. <br/>
/// The saved instruction pointer points to the instruction which caused the exception. <br/>
/// Error code: The General Protection Fault sets an error code, which is the segment selector index when the exception
/// is segment related. Otherwise, 0.
/// </summary>
public class CpuGeneralProtectionFaultException : CpuException {
    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="message">The message describing the error.</param>
    /// <param name="errorCode">Some exceptions may have an error code pushed on the stack.</param>
    public CpuGeneralProtectionFaultException(string message, ushort errorCode = 0)
        : base(message, 0x0D, CpuExceptionType.Fault, "#GP", errorCode) {
    }
}