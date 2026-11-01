namespace Spice86.Core.Emulator.Devices.Sound.Blaster;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Typed wrappers for the Sound Blaster ports registered by <see cref="SoundBlaster"/> at the
/// default base address 0x220 (offsets 0x4-0xF except 0x8 and 0x9, which are the OPL ports). Each
/// method is a thin call to <see cref="IOPortDispatcher"/> at the port it wraps, so the behavior is
/// the same for any configured base.
/// </summary>
public sealed class SoundBlasterPorts {
    private const ushort MixerIndex = 0x224;
    private const ushort MixerData = 0x225;
    private const ushort DspReset = 0x226;
    private const ushort Port227 = 0x227;
    private const ushort DspReadData = 0x22A;
    private const ushort Port22B = 0x22B;
    private const ushort DspWriteCommand = 0x22C;
    private const ushort Port22D = 0x22D;
    private const ushort DspReadStatus = 0x22E;
    private const ushort DspAck16Bit = 0x22F;

    private readonly IOPortDispatcher _dispatcher;

    /// <summary>
    /// Initializes a new instance of the <see cref="SoundBlasterPorts"/> class.
    /// </summary>
    /// <param name="dispatcher">The I/O port dispatcher the wrappers call.</param>
    public SoundBlasterPorts(IOPortDispatcher dispatcher) {
        _dispatcher = dispatcher;
    }

    /// <summary>Reads the mixer index register (0x224).</summary>
    [PortWrapper(MixerIndex, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadMixerIndex() => _dispatcher.ReadByte(MixerIndex);

    /// <summary>Writes the mixer index register (0x224).</summary>
    [PortWrapper(MixerIndex, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteMixerIndex(byte value) => _dispatcher.WriteByte(MixerIndex, value);

    /// <summary>Reads the mixer data register (0x225).</summary>
    [PortWrapper(MixerData, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadMixerData() => _dispatcher.ReadByte(MixerData);

    /// <summary>Writes the mixer data register (0x225).</summary>
    [PortWrapper(MixerData, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteMixerData(byte value) => _dispatcher.WriteByte(MixerData, value);

    /// <summary>Reads the DSP reset port (0x226).</summary>
    [PortWrapper(DspReset, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadDspReset() => _dispatcher.ReadByte(DspReset);

    /// <summary>Writes the DSP reset port (0x226).</summary>
    [PortWrapper(DspReset, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteDspReset(byte value) => _dispatcher.WriteByte(DspReset, value);

    /// <summary>Reads port 0x227.</summary>
    [PortWrapper(Port227, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadPort227() => _dispatcher.ReadByte(Port227);

    /// <summary>Writes port 0x227 (a no-op on the DSP).</summary>
    [PortWrapper(Port227, BitWidth.BYTE_8, PortDirection.Out)]
    public void WritePort227(byte value) => _dispatcher.WriteByte(Port227, value);

    /// <summary>Reads a byte of DSP output data (0x22A).</summary>
    [PortWrapper(DspReadData, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadDspData() => _dispatcher.ReadByte(DspReadData);

    /// <summary>Reads port 0x22B.</summary>
    [PortWrapper(Port22B, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadPort22B() => _dispatcher.ReadByte(Port22B);

    /// <summary>Reads the DSP write buffer status (0x22C).</summary>
    [PortWrapper(DspWriteCommand, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadDspWriteStatus() => _dispatcher.ReadByte(DspWriteCommand);

    /// <summary>Writes a DSP command or data byte (0x22C).</summary>
    [PortWrapper(DspWriteCommand, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteDspData(byte value) => _dispatcher.WriteByte(DspWriteCommand, value);

    /// <summary>Reads port 0x22D.</summary>
    [PortWrapper(Port22D, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadPort22D() => _dispatcher.ReadByte(Port22D);

    /// <summary>Reads the DSP read buffer status, acknowledging the 8-bit IRQ (0x22E).</summary>
    [PortWrapper(DspReadStatus, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadDspReadStatus() => _dispatcher.ReadByte(DspReadStatus);

    /// <summary>Reads the 16-bit IRQ acknowledge port (0x22F).</summary>
    [PortWrapper(DspAck16Bit, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadDspAck16() => _dispatcher.ReadByte(DspAck16Bit);
}
