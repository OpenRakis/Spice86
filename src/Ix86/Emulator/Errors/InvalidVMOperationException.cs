namespace Ix86.Emulator.Errors;
using Ix86.Emulator.Machine;

/// <summary>
/// Base class for exceptions occurring in the VM.<br/>
/// Gives the VM status in the generated error message.
/// </summary>
public class InvalidVMOperationException : Exception
{
    public InvalidVMOperationException(Machine machine, string message) : base(GenerateStatusMessage(machine, message))
    {
    }

    protected static string GenerateStatusMessage(Machine machine, string message)
    {
        string error = "An error occurred while machine was in this state: " + machine.GetCpu().GetState().ToString();
        if (message != null)
        {
            error += $".{Environment.NewLine}Error is: " + message;
        }

        return error;
    }
}
