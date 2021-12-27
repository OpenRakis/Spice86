namespace Ix86.Emulator.Callback;
using Ix86.Emulator.Errors;
using Ix86.Emulator.Machine;
/// <summary>
/// Exception signaling that the callback number that was meant to be executed was not mapped to any java code.<br/>
/// Could happen for unhandled exceptions.
/// </summary>
public class UnhandledCallbackException : UnhandledOperationException
{
    public UnhandledCallbackException(Machine machine, int callbackNumber) : base(machine, FormatMessage(callbackNumber))
    {
    }

    private static string FormatMessage(int callbackNumber)
    {
        return String.Format("callbackNumber=0x%X", callbackNumber);
    }
}
