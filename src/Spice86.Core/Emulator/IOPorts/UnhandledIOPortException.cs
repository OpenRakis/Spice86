namespace Spice86.Core.Emulator.IOPorts;

using System;

using Spice86.Core.Emulator.Errors;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Utils;

/// <summary>
/// Thrown when an unhandled IO Port is accessed.
/// </summary>
[Serializable]
public class UnhandledIOPortException : UnhandledOperationException {
    
    /// <summary>
    /// Initializes a new instance of the <see cref="UnhandledIOPortException"/> class with the specified machine and IO Port number.
    /// </summary>
    /// <param name="machine">The <see cref="Machine"/> instance associated with the exception.</param>
    /// <param name="ioPort">The number of the unhandled IO Port.</param>
    public UnhandledIOPortException(Machine machine, int ioPort) : base(machine, $"Unhandled port {ConvertUtils.ToHex((uint)ioPort)}. This usually means that the hardware behind the port is not emulated or that the port is not routed correctly.") {
    }
}