namespace Spice86.Core.Emulator.CPU.Exceptions;

/// <summary>
/// The Invalid Opcode exception occurs when the processor tries to execute an invalid or undefined opcode, or an
/// instruction with invalid prefixes. It also occurs in other cases, such as:
///    - The instruction length exceeds 15 bytes, but this only occurs with redundant prefixes.
///    - The instruction tries to access a non-existent control register (for example, mov cr6, eax).
///    - The UD instruction is executed. 
/// The saved instruction pointer points to the instruction which caused the exception. 
/// </summary>
public class CpuInvalidOpcodeException : CpuException {
    public CpuInvalidOpcodeException(string message)
        : base(message, 0x06, CpuExceptionType.Fault, "#UD") {
    }
}