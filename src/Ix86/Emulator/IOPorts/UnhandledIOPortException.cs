namespace Ix86.Emulator.IOPorts;

using Ix86.Emulator.Errors;
using Ix86.Emulator.Machine;
using Ix86.Utils;

/// <summary>
/// Thrown when an unhandled IO Port is accessed.
/// </summary>
public class UnhandledIOPortException : UnhandledOperationException
{
    public UnhandledIOPortException(Machine machine, int ioPort) : base(machine, $"Unhandled port {ConvertUtils.ToHex(ioPort)}. This usually means that the hardware behind the port is not emulated or that the port is not routed correctly.")
    {
    }
}
