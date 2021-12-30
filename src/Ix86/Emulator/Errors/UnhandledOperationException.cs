namespace Ix86.Emulator.Errors;
using Ix86.Emulator.Machine;

using System;

/// <summary>
/// Thrown when an unsupported / invalid operation is requested.
/// </summary>
[Serializable]
public class UnhandledOperationException : InvalidVMOperationException
{
    public UnhandledOperationException(Machine machine, string message) : base(machine, message)
    {
    }
}
