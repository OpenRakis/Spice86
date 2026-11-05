namespace Spice86.Core.Emulator.Devices.DirectMemoryAccess;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Typed wrappers for the DMA ports registered by <see cref="DmaBus"/>: the primary 8237 controller
/// (0x00-0x0F), the secondary controller (0xC0-0xDE, even addresses) and the page registers
/// (0x81-0x8F). Each method is a thin call to <see cref="IOPortDispatcher"/> at the port it wraps.
/// </summary>
public sealed class DmaPorts {
    private readonly IOPortDispatcher _dispatcher;

    /// <summary>
    /// Initializes a new instance of the <see cref="DmaPorts"/> class.
    /// </summary>
    /// <param name="dispatcher">The I/O port dispatcher the wrappers call.</param>
    public DmaPorts(IOPortDispatcher dispatcher) {
        _dispatcher = dispatcher;
    }

    /// <summary>Reads the channel 0 current address register (0x00).</summary>
    [PortWrapper(0x00, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadChannel0Address() => _dispatcher.ReadByte(0x00);

    /// <summary>Writes the channel 0 current address register (0x00).</summary>
    [PortWrapper(0x00, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteChannel0Address(byte value) => _dispatcher.WriteByte(0x00, value);

    /// <summary>Reads the channel 0 current count register (0x01).</summary>
    [PortWrapper(0x01, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadChannel0Count() => _dispatcher.ReadByte(0x01);

    /// <summary>Writes the channel 0 current count register (0x01).</summary>
    [PortWrapper(0x01, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteChannel0Count(byte value) => _dispatcher.WriteByte(0x01, value);

    /// <summary>Reads the channel 1 current address register (0x02).</summary>
    [PortWrapper(0x02, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadChannel1Address() => _dispatcher.ReadByte(0x02);

    /// <summary>Writes the channel 1 current address register (0x02).</summary>
    [PortWrapper(0x02, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteChannel1Address(byte value) => _dispatcher.WriteByte(0x02, value);

    /// <summary>Reads the channel 1 current count register (0x03).</summary>
    [PortWrapper(0x03, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadChannel1Count() => _dispatcher.ReadByte(0x03);

    /// <summary>Writes the channel 1 current count register (0x03).</summary>
    [PortWrapper(0x03, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteChannel1Count(byte value) => _dispatcher.WriteByte(0x03, value);

    /// <summary>Reads the channel 2 current address register (0x04).</summary>
    [PortWrapper(0x04, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadChannel2Address() => _dispatcher.ReadByte(0x04);

    /// <summary>Writes the channel 2 current address register (0x04).</summary>
    [PortWrapper(0x04, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteChannel2Address(byte value) => _dispatcher.WriteByte(0x04, value);

    /// <summary>Reads the channel 2 current count register (0x05).</summary>
    [PortWrapper(0x05, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadChannel2Count() => _dispatcher.ReadByte(0x05);

    /// <summary>Writes the channel 2 current count register (0x05).</summary>
    [PortWrapper(0x05, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteChannel2Count(byte value) => _dispatcher.WriteByte(0x05, value);

    /// <summary>Reads the channel 3 current address register (0x06).</summary>
    [PortWrapper(0x06, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadChannel3Address() => _dispatcher.ReadByte(0x06);

    /// <summary>Writes the channel 3 current address register (0x06).</summary>
    [PortWrapper(0x06, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteChannel3Address(byte value) => _dispatcher.WriteByte(0x06, value);

    /// <summary>Reads the channel 3 current count register (0x07).</summary>
    [PortWrapper(0x07, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadChannel3Count() => _dispatcher.ReadByte(0x07);

    /// <summary>Writes the channel 3 current count register (0x07).</summary>
    [PortWrapper(0x07, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteChannel3Count(byte value) => _dispatcher.WriteByte(0x07, value);

    /// <summary>Reads the primary controller status register (0x08).</summary>
    [PortWrapper(0x08, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadPrimaryStatus() => _dispatcher.ReadByte(0x08);

    /// <summary>Writes the primary controller command register (0x08).</summary>
    [PortWrapper(0x08, BitWidth.BYTE_8, PortDirection.Out)]
    public void WritePrimaryCommand(byte value) => _dispatcher.WriteByte(0x08, value);

    /// <summary>Reads the primary controller request register (0x09).</summary>
    [PortWrapper(0x09, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadPort09() => _dispatcher.ReadByte(0x09);

    /// <summary>Writes the primary controller request register (0x09).</summary>
    [PortWrapper(0x09, BitWidth.BYTE_8, PortDirection.Out)]
    public void WritePrimaryRequest(byte value) => _dispatcher.WriteByte(0x09, value);

    /// <summary>Reads the primary controller mask register (0x0A).</summary>
    [PortWrapper(0x0A, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadPort0A() => _dispatcher.ReadByte(0x0A);

    /// <summary>Writes the primary controller single mask register (0x0A).</summary>
    [PortWrapper(0x0A, BitWidth.BYTE_8, PortDirection.Out)]
    public void WritePrimaryMask(byte value) => _dispatcher.WriteByte(0x0A, value);

    /// <summary>Reads the primary controller mode register (0x0B).</summary>
    [PortWrapper(0x0B, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadPort0B() => _dispatcher.ReadByte(0x0B);

    /// <summary>Writes the primary controller mode register (0x0B).</summary>
    [PortWrapper(0x0B, BitWidth.BYTE_8, PortDirection.Out)]
    public void WritePrimaryMode(byte value) => _dispatcher.WriteByte(0x0B, value);

    /// <summary>Reads the primary controller clear flip-flop register (0x0C).</summary>
    [PortWrapper(0x0C, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadPort0C() => _dispatcher.ReadByte(0x0C);

    /// <summary>Writes the primary controller clear flip-flop command (0x0C).</summary>
    [PortWrapper(0x0C, BitWidth.BYTE_8, PortDirection.Out)]
    public void WritePrimaryClearFlipFlop(byte value) => _dispatcher.WriteByte(0x0C, value);

    /// <summary>Reads the primary controller master clear register (0x0D).</summary>
    [PortWrapper(0x0D, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadPort0D() => _dispatcher.ReadByte(0x0D);

    /// <summary>Writes the primary controller master clear command (0x0D).</summary>
    [PortWrapper(0x0D, BitWidth.BYTE_8, PortDirection.Out)]
    public void WritePrimaryMasterClear(byte value) => _dispatcher.WriteByte(0x0D, value);

    /// <summary>Reads the primary controller clear mask register (0x0E).</summary>
    [PortWrapper(0x0E, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadPort0E() => _dispatcher.ReadByte(0x0E);

    /// <summary>Writes the primary controller clear mask command (0x0E).</summary>
    [PortWrapper(0x0E, BitWidth.BYTE_8, PortDirection.Out)]
    public void WritePrimaryClearMask(byte value) => _dispatcher.WriteByte(0x0E, value);

    /// <summary>Reads the primary controller all mask register (0x0F).</summary>
    [PortWrapper(0x0F, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadPort0F() => _dispatcher.ReadByte(0x0F);

    /// <summary>Writes the primary controller all mask register (0x0F).</summary>
    [PortWrapper(0x0F, BitWidth.BYTE_8, PortDirection.Out)]
    public void WritePrimaryMaskAll(byte value) => _dispatcher.WriteByte(0x0F, value);

    /// <summary>Reads the channel 4 current address register (0xC0).</summary>
    [PortWrapper(0xC0, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadChannel4Address() => _dispatcher.ReadByte(0xC0);

    /// <summary>Writes the channel 4 current address register (0xC0).</summary>
    [PortWrapper(0xC0, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteChannel4Address(byte value) => _dispatcher.WriteByte(0xC0, value);

    /// <summary>Reads the channel 4 current count register (0xC2).</summary>
    [PortWrapper(0xC2, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadChannel4Count() => _dispatcher.ReadByte(0xC2);

    /// <summary>Writes the channel 4 current count register (0xC2).</summary>
    [PortWrapper(0xC2, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteChannel4Count(byte value) => _dispatcher.WriteByte(0xC2, value);

    /// <summary>Reads the channel 5 current address register (0xC4).</summary>
    [PortWrapper(0xC4, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadChannel5Address() => _dispatcher.ReadByte(0xC4);

    /// <summary>Writes the channel 5 current address register (0xC4).</summary>
    [PortWrapper(0xC4, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteChannel5Address(byte value) => _dispatcher.WriteByte(0xC4, value);

    /// <summary>Reads the channel 5 current count register (0xC6).</summary>
    [PortWrapper(0xC6, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadChannel5Count() => _dispatcher.ReadByte(0xC6);

    /// <summary>Writes the channel 5 current count register (0xC6).</summary>
    [PortWrapper(0xC6, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteChannel5Count(byte value) => _dispatcher.WriteByte(0xC6, value);

    /// <summary>Reads the channel 6 current address register (0xC8).</summary>
    [PortWrapper(0xC8, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadChannel6Address() => _dispatcher.ReadByte(0xC8);

    /// <summary>Writes the channel 6 current address register (0xC8).</summary>
    [PortWrapper(0xC8, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteChannel6Address(byte value) => _dispatcher.WriteByte(0xC8, value);

    /// <summary>Reads the channel 6 current count register (0xCA).</summary>
    [PortWrapper(0xCA, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadChannel6Count() => _dispatcher.ReadByte(0xCA);

    /// <summary>Writes the channel 6 current count register (0xCA).</summary>
    [PortWrapper(0xCA, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteChannel6Count(byte value) => _dispatcher.WriteByte(0xCA, value);

    /// <summary>Reads the channel 7 current address register (0xCC).</summary>
    [PortWrapper(0xCC, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadChannel7Address() => _dispatcher.ReadByte(0xCC);

    /// <summary>Writes the channel 7 current address register (0xCC).</summary>
    [PortWrapper(0xCC, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteChannel7Address(byte value) => _dispatcher.WriteByte(0xCC, value);

    /// <summary>Reads the channel 7 current count register (0xCE).</summary>
    [PortWrapper(0xCE, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadChannel7Count() => _dispatcher.ReadByte(0xCE);

    /// <summary>Writes the channel 7 current count register (0xCE).</summary>
    [PortWrapper(0xCE, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteChannel7Count(byte value) => _dispatcher.WriteByte(0xCE, value);

    /// <summary>Reads the secondary controller status register (0xD0).</summary>
    [PortWrapper(0xD0, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadSecondaryStatus() => _dispatcher.ReadByte(0xD0);

    /// <summary>Writes the secondary controller command register (0xD0).</summary>
    [PortWrapper(0xD0, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteSecondaryCommand(byte value) => _dispatcher.WriteByte(0xD0, value);

    /// <summary>Reads the secondary controller request register (0xD2).</summary>
    [PortWrapper(0xD2, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadPortD2() => _dispatcher.ReadByte(0xD2);

    /// <summary>Writes the secondary controller request register (0xD2).</summary>
    [PortWrapper(0xD2, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteSecondaryRequest(byte value) => _dispatcher.WriteByte(0xD2, value);

    /// <summary>Reads the secondary controller mask register (0xD4).</summary>
    [PortWrapper(0xD4, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadPortD4() => _dispatcher.ReadByte(0xD4);

    /// <summary>Writes the secondary controller single mask register (0xD4).</summary>
    [PortWrapper(0xD4, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteSecondaryMask(byte value) => _dispatcher.WriteByte(0xD4, value);

    /// <summary>Reads the secondary controller mode register (0xD6).</summary>
    [PortWrapper(0xD6, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadPortD6() => _dispatcher.ReadByte(0xD6);

    /// <summary>Writes the secondary controller mode register (0xD6).</summary>
    [PortWrapper(0xD6, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteSecondaryMode(byte value) => _dispatcher.WriteByte(0xD6, value);

    /// <summary>Reads the secondary controller clear flip-flop register (0xD8).</summary>
    [PortWrapper(0xD8, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadPortD8() => _dispatcher.ReadByte(0xD8);

    /// <summary>Writes the secondary controller clear flip-flop command (0xD8).</summary>
    [PortWrapper(0xD8, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteSecondaryClearFlipFlop(byte value) => _dispatcher.WriteByte(0xD8, value);

    /// <summary>Reads the secondary controller master clear register (0xDA).</summary>
    [PortWrapper(0xDA, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadPortDA() => _dispatcher.ReadByte(0xDA);

    /// <summary>Writes the secondary controller master clear command (0xDA).</summary>
    [PortWrapper(0xDA, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteSecondaryMasterClear(byte value) => _dispatcher.WriteByte(0xDA, value);

    /// <summary>Reads the secondary controller clear mask register (0xDC).</summary>
    [PortWrapper(0xDC, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadPortDC() => _dispatcher.ReadByte(0xDC);

    /// <summary>Writes the secondary controller clear mask command (0xDC).</summary>
    [PortWrapper(0xDC, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteSecondaryClearMask(byte value) => _dispatcher.WriteByte(0xDC, value);

    /// <summary>Reads the secondary controller all mask register (0xDE).</summary>
    [PortWrapper(0xDE, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadPortDE() => _dispatcher.ReadByte(0xDE);

    /// <summary>Writes the secondary controller all mask register (0xDE).</summary>
    [PortWrapper(0xDE, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteSecondaryMaskAll(byte value) => _dispatcher.WriteByte(0xDE, value);

    /// <summary>Reads the channel 0 page register (0x87).</summary>
    [PortWrapper(0x87, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadChannel0Page() => _dispatcher.ReadByte(0x87);

    /// <summary>Writes the channel 0 page register (0x87).</summary>
    [PortWrapper(0x87, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteChannel0Page(byte value) => _dispatcher.WriteByte(0x87, value);

    /// <summary>Reads the channel 1 page register (0x83).</summary>
    [PortWrapper(0x83, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadChannel1Page() => _dispatcher.ReadByte(0x83);

    /// <summary>Writes the channel 1 page register (0x83).</summary>
    [PortWrapper(0x83, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteChannel1Page(byte value) => _dispatcher.WriteByte(0x83, value);

    /// <summary>Reads the channel 2 page register (0x81).</summary>
    [PortWrapper(0x81, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadChannel2Page() => _dispatcher.ReadByte(0x81);

    /// <summary>Writes the channel 2 page register (0x81).</summary>
    [PortWrapper(0x81, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteChannel2Page(byte value) => _dispatcher.WriteByte(0x81, value);

    /// <summary>Reads the channel 3 page register (0x82).</summary>
    [PortWrapper(0x82, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadChannel3Page() => _dispatcher.ReadByte(0x82);

    /// <summary>Writes the channel 3 page register (0x82).</summary>
    [PortWrapper(0x82, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteChannel3Page(byte value) => _dispatcher.WriteByte(0x82, value);

    /// <summary>Reads the channel 4 page register (0x8F).</summary>
    [PortWrapper(0x8F, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadChannel4Page() => _dispatcher.ReadByte(0x8F);

    /// <summary>Writes the channel 4 page register (0x8F).</summary>
    [PortWrapper(0x8F, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteChannel4Page(byte value) => _dispatcher.WriteByte(0x8F, value);

    /// <summary>Reads the channel 5 page register (0x8B).</summary>
    [PortWrapper(0x8B, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadChannel5Page() => _dispatcher.ReadByte(0x8B);

    /// <summary>Writes the channel 5 page register (0x8B).</summary>
    [PortWrapper(0x8B, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteChannel5Page(byte value) => _dispatcher.WriteByte(0x8B, value);

    /// <summary>Reads the channel 6 page register (0x89).</summary>
    [PortWrapper(0x89, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadChannel6Page() => _dispatcher.ReadByte(0x89);

    /// <summary>Writes the channel 6 page register (0x89).</summary>
    [PortWrapper(0x89, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteChannel6Page(byte value) => _dispatcher.WriteByte(0x89, value);

    /// <summary>Reads the channel 7 page register (0x8A).</summary>
    [PortWrapper(0x8A, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadChannel7Page() => _dispatcher.ReadByte(0x8A);

    /// <summary>Writes the channel 7 page register (0x8A).</summary>
    [PortWrapper(0x8A, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteChannel7Page(byte value) => _dispatcher.WriteByte(0x8A, value);
}
