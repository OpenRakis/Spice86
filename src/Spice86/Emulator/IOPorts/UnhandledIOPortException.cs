namespace Spice86.Emulator.IOPorts;

using Spice86.Emulator.Errors;
using Spice86.Emulator.VM;
using Spice86.Utils;

/// <summary> Thrown when an unhandled IO Port is accessed. </summary>
public class UnhandledIOPortException : UnhandledOperationException {

    public UnhandledIOPortException(Machine machine, int ioPort) : base(machine, $"Unhandled port {ConvertUtils.ToHex(ioPort)}. This usually means that the hardware behind the port is not emulated or that the port is not routed correctly.") {
    }
}