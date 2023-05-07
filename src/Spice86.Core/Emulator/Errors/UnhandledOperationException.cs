namespace Spice86.Core.Emulator.Errors;

using Spice86.Core.Emulator.VM;

using System;

/// <summary>
/// Exception thrown when an unsupported or invalid operation is requested.
/// </summary>
[Serializable]
public class UnhandledOperationException : InvalidVMOperationException {
    /// <summary>
    /// Initializes a new instance of the <see cref="UnhandledOperationException"/> class with the specified error message and machine.
    /// </summary>
    /// <param name="machine">The machine where the exception occurred.</param>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    public UnhandledOperationException(Machine machine, string message) : base(machine, message) {
    }
}