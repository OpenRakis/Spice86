namespace Spice86.Core.Emulator.Devices.Sound.Midi;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Typed wrappers for the MPU-401 compatible MIDI ports registered by <see cref="Midi"/>. Each
/// method is a thin call to <see cref="IOPortDispatcher"/> at the port it wraps.
/// </summary>
public sealed class MidiPorts {
    private readonly IOPortDispatcher _dispatcher;

    /// <summary>
    /// Initializes a new instance of the <see cref="MidiPorts"/> class.
    /// </summary>
    /// <param name="dispatcher">The I/O port dispatcher the wrappers call.</param>
    public MidiPorts(IOPortDispatcher dispatcher) {
        _dispatcher = dispatcher;
    }

    /// <summary>Reads the MIDI data port (0x330).</summary>
    [PortWrapper(Midi.Data, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadData() => _dispatcher.ReadByte(Midi.Data);

    /// <summary>Writes a MIDI byte (0x330).</summary>
    [PortWrapper(Midi.Data, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteData(byte value) => _dispatcher.WriteByte(Midi.Data, value);

    /// <summary>Reads the MIDI status port (0x331).</summary>
    [PortWrapper(Midi.Command, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadStatus() => _dispatcher.ReadByte(Midi.Command);

    /// <summary>Writes a MIDI command (0x331).</summary>
    [PortWrapper(Midi.Command, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteCommand(byte value) => _dispatcher.WriteByte(Midi.Command, value);
}
