namespace Spice86.Core.Emulator.Devices.Timer;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Typed wrappers for the programmable interval timer ports registered by <see cref="PitTimer"/>.
/// Each method is a thin call to <see cref="IOPortDispatcher"/> at the port it wraps.
/// </summary>
public sealed class PitPorts {
    private const ushort Channel0Port = 0x40;
    private const ushort Channel1Port = 0x41;
    private const ushort Channel2Port = 0x42;
    private const ushort ControlPort = 0x43;

    private readonly IOPortDispatcher _dispatcher;

    /// <summary>
    /// Initializes a new instance of the <see cref="PitPorts"/> class.
    /// </summary>
    /// <param name="dispatcher">The I/O port dispatcher the wrappers call.</param>
    public PitPorts(IOPortDispatcher dispatcher) {
        _dispatcher = dispatcher;
    }

    /// <summary>Reads the channel 0 counter (0x40).</summary>
    [PortWrapper(Channel0Port, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadChannel0Data() => _dispatcher.ReadByte(Channel0Port);

    /// <summary>Writes the channel 0 reload value (0x40).</summary>
    [PortWrapper(Channel0Port, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteChannel0Data(byte value) => _dispatcher.WriteByte(Channel0Port, value);

    /// <summary>Reads the channel 1 counter (0x41).</summary>
    [PortWrapper(Channel1Port, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadChannel1Data() => _dispatcher.ReadByte(Channel1Port);

    /// <summary>Reads the channel 2 counter (0x42).</summary>
    [PortWrapper(Channel2Port, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadChannel2Data() => _dispatcher.ReadByte(Channel2Port);

    /// <summary>Writes the channel 2 reload value (0x42).</summary>
    [PortWrapper(Channel2Port, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteChannel2Data(byte value) => _dispatcher.WriteByte(Channel2Port, value);

    /// <summary>Writes a control word to the mode/control register (0x43).</summary>
    [PortWrapper(ControlPort, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteControlWord(byte value) => _dispatcher.WriteByte(ControlPort, value);
}
