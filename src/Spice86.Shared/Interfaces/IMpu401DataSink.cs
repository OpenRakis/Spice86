namespace Spice86.Shared.Interfaces;

/// <summary>
/// Narrow sink used by <c>MidiOnGameportRouter</c> to forward bytes
/// to the MPU-401 data port without depending on the concrete MIDI
/// device implementation.
/// </summary>
/// <remarks>
/// The wiring layer (composition root) provides an implementation
/// that calls the MPU-401's data-port write entry point. Tests
/// substitute a fake to capture forwarded bytes.
/// </remarks>
public interface IMpu401DataSink {
    /// <summary>
    /// Writes a single byte to the MPU-401 data port at the given
    /// base address (data-port = base + 0).
    /// </summary>
    /// <param name="basePort">MPU-401 base I/O port (typically
    /// <c>0x330</c>).</param>
    /// <param name="value">Byte to forward.</param>
    void WriteData(int basePort, byte value);
}
