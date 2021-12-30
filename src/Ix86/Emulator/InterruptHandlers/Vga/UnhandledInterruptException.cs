namespace Ix86.Emulator.InterruptHandlers.Vga;

using Ix86.Emulator.Errors;
using Ix86.Emulator.Machine;

/// <summary>
/// Signals that the operation for the given callback is not handled.
/// </summary>
public class UnhandledInterruptException : UnhandledOperationException
{
    public UnhandledInterruptException(Machine machine, int callbackNumber, int operation) : base(machine, FormatMessage(callbackNumber, operation))
    {
    }

    private static string FormatMessage(int callbackNumber, int operation)
    {
        return $"callbackNumber=0x{callbackNumber:X}, operation=0x{operation:X}";
    }
}
