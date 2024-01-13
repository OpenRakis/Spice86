namespace Spice86.Core.Emulator.Errors;

using Spice86.Core.Emulator.CPU;

/// <summary>
/// Exception thrown when an unsupported or invalid operation is requested.
/// </summary>
public class UnhandledOperationException : InvalidVMOperationException {
    /// <summary>
    /// Initializes a new instance of the <see cref="UnhandledOperationException"/> class.
    /// </summary>
    /// <param name="state">The CPU state when the exception occured.</param>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    public UnhandledOperationException(State state, string message) : base(state, message) {
    }
}