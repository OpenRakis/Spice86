namespace Spice86.Core.Emulator.IOPorts;

using System;

using Spice86.Core.Emulator.Errors;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Utils;

/// <summary> Thrown when an unhandled IO Port is accessed. </summary>
[Serializable]
public class UnhandledIOPortException : UnhandledOperationException {
    public UnhandledIOPortException(Machine machine, int ioPort) : base(machine, $"Unhandled port {ConvertUtils.ToHex((uint)ioPort)}. This usually means that the hardware behind the port is not emulated or that the port is not routed correctly.") {
    }
}