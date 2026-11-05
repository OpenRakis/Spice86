namespace Spice86.Core.Emulator.Devices.ExternalInput;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Typed wrappers for the floppy disk controller ports registered by
/// <see cref="FloppyDiskController"/>. Each method is a thin call to <see cref="IOPortDispatcher"/>
/// at the port it wraps.
/// </summary>
public sealed class FloppyControllerPorts {
    private const ushort PortDor = 0x3F2;
    private const ushort PortMsr = 0x3F4;
    private const ushort PortData = 0x3F5;

    private readonly IOPortDispatcher _dispatcher;

    /// <summary>
    /// Initializes a new instance of the <see cref="FloppyControllerPorts"/> class.
    /// </summary>
    /// <param name="dispatcher">The I/O port dispatcher the wrappers call.</param>
    public FloppyControllerPorts(IOPortDispatcher dispatcher) {
        _dispatcher = dispatcher;
    }

    /// <summary>Writes the digital output register (0x3F2).</summary>
    [PortWrapper(PortDor, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteDigitalOutput(byte value) => _dispatcher.WriteByte(PortDor, value);

    /// <summary>Reads the main status register (0x3F4).</summary>
    [PortWrapper(PortMsr, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadMainStatus() => _dispatcher.ReadByte(PortMsr);

    /// <summary>Reads the data FIFO (0x3F5).</summary>
    [PortWrapper(PortData, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadData() => _dispatcher.ReadByte(PortData);

    /// <summary>Writes the data FIFO (0x3F5).</summary>
    [PortWrapper(PortData, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteData(byte value) => _dispatcher.WriteByte(PortData, value);
}
