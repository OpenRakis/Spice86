namespace Spice86.Core.Emulator.Devices.Cmos;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Typed wrappers for the CMOS/RTC ports registered by <see cref="RealTimeClock"/>. Each method is a
/// thin call to <see cref="IOPortDispatcher"/> at the port of <see cref="CmosPorts"/> it wraps.
/// </summary>
public sealed class RealTimeClockPorts {
    private readonly IOPortDispatcher _dispatcher;

    /// <summary>
    /// Initializes a new instance of the <see cref="RealTimeClockPorts"/> class.
    /// </summary>
    /// <param name="dispatcher">The I/O port dispatcher the wrappers call.</param>
    public RealTimeClockPorts(IOPortDispatcher dispatcher) {
        _dispatcher = dispatcher;
    }

    /// <summary>Writes the CMOS address/index port, selecting the register to access (0x70).</summary>
    [PortWrapper(CmosPorts.Address, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteAddress(byte value) => _dispatcher.WriteByte(CmosPorts.Address, value);

    /// <summary>Reads the CMOS data port of the selected register (0x71).</summary>
    [PortWrapper(CmosPorts.Data, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadData() => _dispatcher.ReadByte(CmosPorts.Data);

    /// <summary>Writes the CMOS data port of the selected register (0x71).</summary>
    [PortWrapper(CmosPorts.Data, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteData(byte value) => _dispatcher.WriteByte(CmosPorts.Data, value);
}
