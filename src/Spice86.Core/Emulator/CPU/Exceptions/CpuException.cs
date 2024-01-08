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
    /// This is the official code for the exception from the Intel manual.
    /// </summary>
    public string Mnemonic { get; }

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="message">The message describing the error.</param>
    /// <param name="interruptVector">The interrupt vector to trigger when this exception occurs.</param>
    /// <param name="type">The type of exception: Fault, Trap or Abort.</param>
    /// <param name="mnemonic">This is the official code for the exception from the Intel manual.</param>
    /// <param name="errorCode">Some exceptions may have an error code pushed on the stack.</param>
    protected CpuException(string message, byte interruptVector, CpuExceptionType type, string mnemonic,
        ushort? errorCode = null) : base(message) {
        InterruptVector = interruptVector;
        Type = type;
        Mnemonic = mnemonic;
        ErrorCode = errorCode;
    }

    /// <inheritdoc />
    public override string ToString() {
        return $"CPU Exception: {Type}: {Mnemonic} - {Message} [{ErrorCode:X4}]";
    }
}