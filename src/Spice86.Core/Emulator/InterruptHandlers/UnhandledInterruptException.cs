namespace Spice86.Core.Emulator.InterruptHandlers;

using System;

using Spice86.Core.Emulator.Errors;
using Spice86.Core.Emulator.VM;

/// <summary> Signals that the operation for the given callback is not handled. </summary>
[Serializable]
public class UnhandledInterruptException : UnhandledOperationException {
    public UnhandledInterruptException(Machine machine, int callbackNumber, int operation) : base(machine, FormatMessage(callbackNumber, operation)) {
    }

    private static string FormatMessage(int callbackNumber, int operation) {
        return $"callbackNumber=0x{callbackNumber:X}, operation=0x{operation:X}";
    }
}