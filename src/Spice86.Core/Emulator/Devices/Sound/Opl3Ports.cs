namespace Spice86.Core.Emulator.Devices.Sound;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Typed wrappers for the OPL ports registered by <see cref="Opl3Fm"/>: the AdLib ports
/// (0x388-0x38B) and the ports on the Sound Blaster base address 0x220 (0x220-0x223 and
/// 0x228-0x229). Even ports are the address/index registers, odd ports the data registers. Each
/// method is a thin call to <see cref="IOPortDispatcher"/> at the port it wraps, so the behavior is
/// the same for any configured base.
/// </summary>
public sealed class Opl3Ports {
    private const ushort SbAddress0 = 0x220;
    private const ushort SbData0 = 0x221;
    private const ushort SbAddress1 = 0x222;
    private const ushort SbData1 = 0x223;
    private const ushort SbAddress2 = 0x228;
    private const ushort SbData2 = 0x229;
    private const ushort AdlibAddress0 = 0x388;
    private const ushort AdlibData0 = 0x389;
    private const ushort AdlibAddress1 = 0x38A;
    private const ushort AdlibData1 = 0x38B;

    private readonly IOPortDispatcher _dispatcher;

    /// <summary>
    /// Initializes a new instance of the <see cref="Opl3Ports"/> class.
    /// </summary>
    /// <param name="dispatcher">The I/O port dispatcher the wrappers call.</param>
    public Opl3Ports(IOPortDispatcher dispatcher) {
        _dispatcher = dispatcher;
    }

    /// <summary>Reads the OPL address register on the Sound Blaster base (0x220).</summary>
    [PortWrapper(SbAddress0, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadSbAddress0() => _dispatcher.ReadByte(SbAddress0);

    /// <summary>Writes the OPL address register on the Sound Blaster base (0x220).</summary>
    [PortWrapper(SbAddress0, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteSbAddress0(byte value) => _dispatcher.WriteByte(SbAddress0, value);

    /// <summary>Reads the OPL data register on the Sound Blaster base (0x221).</summary>
    [PortWrapper(SbData0, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadSbData0() => _dispatcher.ReadByte(SbData0);

    /// <summary>Writes the OPL data register on the Sound Blaster base (0x221).</summary>
    [PortWrapper(SbData0, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteSbData0(byte value) => _dispatcher.WriteByte(SbData0, value);

    /// <summary>Reads the second OPL address register on the Sound Blaster base (0x222).</summary>
    [PortWrapper(SbAddress1, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadSbAddress1() => _dispatcher.ReadByte(SbAddress1);

    /// <summary>Writes the second OPL address register on the Sound Blaster base (0x222).</summary>
    [PortWrapper(SbAddress1, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteSbAddress1(byte value) => _dispatcher.WriteByte(SbAddress1, value);

    /// <summary>Reads the second OPL data register on the Sound Blaster base (0x223).</summary>
    [PortWrapper(SbData1, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadSbData1() => _dispatcher.ReadByte(SbData1);

    /// <summary>Writes the second OPL data register on the Sound Blaster base (0x223).</summary>
    [PortWrapper(SbData1, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteSbData1(byte value) => _dispatcher.WriteByte(SbData1, value);

    /// <summary>Reads the advanced OPL address register on the Sound Blaster base (0x228).</summary>
    [PortWrapper(SbAddress2, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadSbAddress2() => _dispatcher.ReadByte(SbAddress2);

    /// <summary>Writes the advanced OPL address register on the Sound Blaster base (0x228).</summary>
    [PortWrapper(SbAddress2, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteSbAddress2(byte value) => _dispatcher.WriteByte(SbAddress2, value);

    /// <summary>Reads the advanced OPL data register on the Sound Blaster base (0x229).</summary>
    [PortWrapper(SbData2, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadSbData2() => _dispatcher.ReadByte(SbData2);

    /// <summary>Writes the advanced OPL data register on the Sound Blaster base (0x229).</summary>
    [PortWrapper(SbData2, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteSbData2(byte value) => _dispatcher.WriteByte(SbData2, value);

    /// <summary>Reads the AdLib OPL address register (0x388).</summary>
    [PortWrapper(AdlibAddress0, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadAdlibAddress0() => _dispatcher.ReadByte(AdlibAddress0);

    /// <summary>Writes the AdLib OPL address register (0x388).</summary>
    [PortWrapper(AdlibAddress0, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteAdlibAddress0(byte value) => _dispatcher.WriteByte(AdlibAddress0, value);

    /// <summary>Reads the AdLib OPL data register (0x389).</summary>
    [PortWrapper(AdlibData0, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadAdlibData0() => _dispatcher.ReadByte(AdlibData0);

    /// <summary>Writes the AdLib OPL data register (0x389).</summary>
    [PortWrapper(AdlibData0, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteAdlibData0(byte value) => _dispatcher.WriteByte(AdlibData0, value);

    /// <summary>Reads the second AdLib OPL address register (0x38A).</summary>
    [PortWrapper(AdlibAddress1, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadAdlibAddress1() => _dispatcher.ReadByte(AdlibAddress1);

    /// <summary>Writes the second AdLib OPL address register (0x38A).</summary>
    [PortWrapper(AdlibAddress1, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteAdlibAddress1(byte value) => _dispatcher.WriteByte(AdlibAddress1, value);

    /// <summary>Reads the second AdLib OPL data register (0x38B).</summary>
    [PortWrapper(AdlibData1, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadAdlibData1() => _dispatcher.ReadByte(AdlibData1);

    /// <summary>Writes the second AdLib OPL data register (0x38B).</summary>
    [PortWrapper(AdlibData1, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteAdlibData1(byte value) => _dispatcher.WriteByte(AdlibData1, value);
}
