namespace Spice86.Core.Emulator.CPU.Exceptions;

/// <summary>
/// Base class for CPU exceptions.
/// https://wiki.osdev.org/Exceptions
/// </summary>
public abstract class CpuException : Exception {
    /// <summary>
    /// The interrupt vector to trigger when this exception occurs.
    /// </summary>
    public byte InterruptVector { get; }

    /// <summary>
    /// The type of exception, Fault, Trap or Abort.
    /// This determines when the exception occur and how it is handled.  
    /// </summary>
    public CpuExceptionType Type { get; }

    /// <summary>
    /// Some exceptions may have an error code pushed on the stack.
    /// </summary>
    public ushort? ErrorCode { get; }

    /// <summary>
    /// This is the official code for the exception from the intel manual.
    /// </summary>
    public string Mnemonic { get; }

    protected CpuException(string message, byte interruptVector, CpuExceptionType type, string mnemonic,
        ushort? errorCode = null) : base(message) {
        InterruptVector = interruptVector;
        Type = type;
        Mnemonic = mnemonic;
        ErrorCode = errorCode;
    }

    public override string ToString() {
        return $"CPU Exception: {Type}: {Mnemonic} - {Message} [{ErrorCode:X4}]";
    }
}