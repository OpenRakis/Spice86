namespace Spice86.Core.Emulator.Errors;

using Spice86.Core.Emulator.VM;

using System;

/// <summary>
/// Base class for exceptions occurring in the VM. <br /> Gives the VM status in the generated error
/// message. Named <see cref="InvalidVMOperationException" /> because
/// <see cref="InvalidOperationException" /> already exists in the BCL.
/// </summary>
[Serializable]
public class InvalidVMOperationException : Exception {
    /// <summary>
    /// Constructs a new instance of <see cref="InvalidVMOperationException"/> with the specified error message
    /// and the current state of the <paramref name="machine"/>.
    /// </summary>
    /// <param name="machine">The <see cref="Machine"/> instance.</param>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    public InvalidVMOperationException(Machine machine, string message) : base(GenerateStatusMessage(machine, message)) {
    }

    /// <summary>
    /// Constructs a new instance of <see cref="InvalidVMOperationException"/> with the specified inner exception
    /// and the current state of the <paramref name="machine"/>.
    /// </summary>
    /// <param name="machine">The <see cref="Machine"/> instance.</param>
    /// <param name="e">The inner exception that caused this exception to be thrown.</param>
    public InvalidVMOperationException(Machine machine, Exception e) : base(GenerateStatusMessage(machine, e.Message), e) {
    }

    /// <summary>
    /// Generates a status message that includes the current state of the <paramref name="machine"/>
    /// and an optional error message.
    /// </summary>
    /// <param name="machine">The <see cref="Machine"/> instance.</param>
    /// <param name="message">An optional error message to include in the status message.</param>
    /// <returns>The generated status message.</returns>
    protected static string GenerateStatusMessage(Machine machine, string? message) {
        string error = $"An error occurred while machine was in this state: {machine.Cpu.State}";
        if (message != null) {
            error += $".{Environment.NewLine}Error is: {message}";
        }

        return error;
    }
}