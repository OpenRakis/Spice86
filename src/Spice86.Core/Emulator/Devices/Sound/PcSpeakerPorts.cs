namespace Spice86.Core.Emulator.Devices.Sound;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Typed wrappers for the PPI port B registered by <see cref="PcSpeaker"/>; it gates the PC speaker
/// output and the PIT channel 2 timer. Each method is a thin call to
/// <see cref="IOPortDispatcher"/> at port 0x61.
/// </summary>
public sealed class PcSpeakerPorts {
    private const ushort PortB = 0x61;

    private readonly IOPortDispatcher _dispatcher;

    /// <summary>
    /// Initializes a new instance of the <see cref="PcSpeakerPorts"/> class.
    /// </summary>
    /// <param name="dispatcher">The I/O port dispatcher the wrappers call.</param>
    public PcSpeakerPorts(IOPortDispatcher dispatcher) {
        _dispatcher = dispatcher;
    }

    /// <summary>Reads PPI port B (0x61).</summary>
    [PortWrapper(PortB, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadPortB() => _dispatcher.ReadByte(PortB);

    /// <summary>Writes PPI port B (0x61).</summary>
    [PortWrapper(PortB, BitWidth.BYTE_8, PortDirection.Out)]
    public void WritePortB(byte value) => _dispatcher.WriteByte(PortB, value);
}
