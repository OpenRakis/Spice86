namespace Spice86.Emulator.InterruptHandlers;

using Spice86.Emulator.Errors;
using Spice86.Emulator.VM;

/// <summary> Signals that the operation for the given callback is not handled. </summary>
public class UnhandledInterruptException : UnhandledOperationException {

    public UnhandledInterruptException(Machine machine, int callbackNumber, int operation) : base(machine, FormatMessage(callbackNumber, operation)) {
    }

    private static string FormatMessage(int callbackNumber, int operation) {
        return $"callbackNumber=0x{callbackNumber:X}, operation=0x{operation:X}";
    }
}