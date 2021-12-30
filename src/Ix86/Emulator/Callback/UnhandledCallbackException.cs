namespace Ix86.Emulator.Callback;
using Ix86.Emulator.Errors;
using Ix86.Emulator.Machine;

using System;

/// <summary>
/// Exception signaling that the callback number that was meant to be executed was not mapped to any csharp code.<br/>
/// Could happen for unhandled exceptions.
/// </summary>
[Serializable]
public class UnhandledCallbackException : UnhandledOperationException
{
    public UnhandledCallbackException(Machine machine, int callbackNumber) : base(machine, FormatMessage(callbackNumber))
    {
    }

    private static string FormatMessage(int callbackNumber)
    {
        return $"callbackNumber=0x{callbackNumber:x}";
    }
}
