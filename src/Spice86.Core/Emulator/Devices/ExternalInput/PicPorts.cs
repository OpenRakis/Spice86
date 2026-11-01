namespace Spice86.Core.Emulator.Devices.ExternalInput;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Typed wrappers for the interrupt controller ports registered by <see cref="DualPic"/>. Each
/// method is a thin call to <see cref="IOPortDispatcher"/> at the port it wraps.
/// </summary>
public sealed class PicPorts {
    private const ushort PrimaryCommandPort = 0x20;
    private const ushort PrimaryDataPort = 0x21;
    private const ushort SecondaryCommandPort = 0xA0;
    private const ushort SecondaryDataPort = 0xA1;

    private readonly IOPortDispatcher _dispatcher;

    /// <summary>
    /// Initializes a new instance of the <see cref="PicPorts"/> class.
    /// </summary>
    /// <param name="dispatcher">The I/O port dispatcher the wrappers call.</param>
    public PicPorts(IOPortDispatcher dispatcher) {
        _dispatcher = dispatcher;
    }

    /// <summary>Reads the primary PIC command port (0x20).</summary>
    [PortWrapper(PrimaryCommandPort, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadPrimaryCommand() => _dispatcher.ReadByte(PrimaryCommandPort);

    /// <summary>Writes the primary PIC command port (0x20).</summary>
    [PortWrapper(PrimaryCommandPort, BitWidth.BYTE_8, PortDirection.Out)]
    public void WritePrimaryCommand(byte value) => _dispatcher.WriteByte(PrimaryCommandPort, value);

    /// <summary>Reads the primary PIC data port (0x21).</summary>
    [PortWrapper(PrimaryDataPort, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadPrimaryData() => _dispatcher.ReadByte(PrimaryDataPort);

    /// <summary>Writes the primary PIC data port (0x21).</summary>
    [PortWrapper(PrimaryDataPort, BitWidth.BYTE_8, PortDirection.Out)]
    public void WritePrimaryData(byte value) => _dispatcher.WriteByte(PrimaryDataPort, value);

    /// <summary>Reads the secondary PIC command port (0xA0).</summary>
    [PortWrapper(SecondaryCommandPort, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadSecondaryCommand() => _dispatcher.ReadByte(SecondaryCommandPort);

    /// <summary>Writes the secondary PIC command port (0xA0).</summary>
    [PortWrapper(SecondaryCommandPort, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteSecondaryCommand(byte value) => _dispatcher.WriteByte(SecondaryCommandPort, value);

    /// <summary>Reads the secondary PIC data port (0xA1).</summary>
    [PortWrapper(SecondaryDataPort, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadSecondaryData() => _dispatcher.ReadByte(SecondaryDataPort);

    /// <summary>Writes the secondary PIC data port (0xA1).</summary>
    [PortWrapper(SecondaryDataPort, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteSecondaryData(byte value) => _dispatcher.WriteByte(SecondaryDataPort, value);
}
