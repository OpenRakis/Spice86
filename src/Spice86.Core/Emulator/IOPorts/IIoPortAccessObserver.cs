namespace Spice86.Core.Emulator.IOPorts;

/// <summary>
/// Observes every write the <see cref="IOPortDispatcher"/> performs, whatever its target: a
/// registered port handler or the unhandled-port fallback. Reads are not reported: poll loops make
/// their count timing-dependent.
/// </summary>
public interface IIoPortAccessObserver {
    /// <summary>Called for every port write, in write order.</summary>
    /// <param name="port">Port written.</param>
    /// <param name="value">Value written.</param>
    /// <param name="width">Write width in bits: 8, 16 or 32.</param>
    void OnWrite(ushort port, uint value, int width);
}
