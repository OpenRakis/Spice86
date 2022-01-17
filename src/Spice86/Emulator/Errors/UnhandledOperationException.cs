namespace Spice86.Emulator.Errors;

using Spice86.Emulator.VM;

using System;

/// <summary> Thrown when an unsupported / invalid operation is requested. </summary>
[Serializable]
public class UnhandledOperationException : InvalidVMOperationException {

    public UnhandledOperationException(Machine machine, string message) : base(machine, message) {
    }
}