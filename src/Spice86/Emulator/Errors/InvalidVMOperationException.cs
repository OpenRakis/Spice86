namespace Spice86.Emulator.Errors;

using Spice86.Emulator.VM;

using System;

/// <summary>
/// Base class for exceptions occurring in the VM. <br /> Gives the VM status in the generated error
/// message. Named <see cref="InvalidVMOperationException" /> because
/// <see cref="InvalidOperationException" /> already exists in the BCL.
/// </summary>
[Serializable]
public class InvalidVMOperationException : Exception {

    public InvalidVMOperationException(Machine machine, string message) : base(GenerateStatusMessage(machine, message)) {
    }

    public InvalidVMOperationException(Machine machine, Exception e) : base(GenerateStatusMessage(machine, e.Message), e) {
    }

    protected static string GenerateStatusMessage(Machine machine, string? message) {
        string error = $"An error occurred while machine was in this state: {machine.GetCpu().GetState()}";
        if (message != null) {
            error += $".{Environment.NewLine}Error is: {message}";
        }

        return error;
    }
}