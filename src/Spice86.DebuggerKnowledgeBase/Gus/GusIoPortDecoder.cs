namespace Spice86.DebuggerKnowledgeBase.Gus;

using System.Collections.Generic;

using Spice86.DebuggerKnowledgeBase.Decoding;

/// <summary>
/// Decodes Gravis Ultrasound (GF1) I/O port reads and writes for every standard card
/// base address (0x210 / 0x220 / 0x230 / 0x240 / 0x250 / 0x260 / 0x270). The decoder
/// covers the full GUS port window mirroring dosbox-staging's <c>gus.cpp</c>: the
/// "low" mixer / IRQ-DMA / AdLib-compat block at base+0x00..0x0F and the "GF1"
/// MIDI / page / register-select / data / DRAM block at base+0x100..0x107.
/// </summary>
/// <remarks>
/// <para>
/// Spice86 currently emulates an absent GUS card (see
/// <c>Spice86.Core.Emulator.Devices.Sound.GravisUltraSound</c>) but software still
/// probes its ports during card detection; decoding those probes makes them legible
/// in the debugger UI. The decoder is pure / read-only: it never touches emulator
/// state and does not know which voice is currently selected via the page register
/// (2X2 / 3X2) or which GF1 register is selected via 2X3 / 3X3 — those indices are
/// described as "selected via the page/register-select port" rather than dereferenced.
/// </para>
/// <para>
/// On the data port (3X4 / 3X5) the decoder names the most recently selected GF1
/// register if the access width carries enough bits to reconstruct it (16-bit
/// access at 3X4); otherwise the byte is documented generically.
/// </para>
/// </remarks>
public sealed class GusIoPortDecoder : IIoPortDecoder {
    private const string Subsystem = "Gravis Ultrasound I/O Ports";

    private readonly ushort _base;

    /// <summary>
    /// Creates a decoder for the GUS card configured at the given <paramref name="basePort"/>
    /// (one of the entries in <see cref="GusDecodingTables.StandardBases"/>).
    /// </summary>
    /// <param name="basePort">Base I/O address of the card (e.g. 0x240).</param>
    public GusIoPortDecoder(ushort basePort) {
        _base = basePort;
    }

    /// <inheritdoc />
    public bool CanDecode(ushort port) {
        int offset = port - _base;
        if (offset < 0) {
            return false;
        }
        return GusDecodingTables.ClassifyOffset(offset) != GusDecodingTables.GusOffset.Unknown;
    }

    /// <inheritdoc />
    public DecodedCall DecodeRead(ushort port, uint value, int width) {
        return Decode(port, value, width, IoPortAccessDirection.Read);
    }

    /// <inheritdoc />
    public DecodedCall DecodeWrite(ushort port, uint value, int width) {
        return Decode(port, value, width, IoPortAccessDirection.Write);
    }

    private DecodedCall Decode(ushort port, uint value, int width, IoPortAccessDirection direction) {
        int offset = port - _base;
        GusDecodingTables.GusOffset cls = GusDecodingTables.ClassifyOffset(offset);
        switch (cls) {
            case GusDecodingTables.GusOffset.MixControl:
                return DecodeMixControl(port, value, width, direction);
            case GusDecodingTables.GusOffset.IrqStatus:
                return DecodeIrqStatus(port, value, width, direction);
            case GusDecodingTables.GusOffset.AdlibTimerCommand:
                return DecodeAdlibTimerCommand(port, value, width, direction);
            case GusDecodingTables.GusOffset.AdlibTimerData:
                return DecodeAdlibTimerData(port, value, width, direction);
            case GusDecodingTables.GusOffset.AdlibCommand:
                return DecodeAdlibCommand(port, value, width, direction);
            case GusDecodingTables.GusOffset.IrqDmaControlSet:
                return DecodeIrqDmaControlSet(port, value, width, direction);
            case GusDecodingTables.GusOffset.RegisterControlsSelect:
                return DecodeRegisterControlsSelect(port, value, width, direction);
            case GusDecodingTables.GusOffset.MidiControl:
                return DecodeMidiControl(port, value, width, direction);
            case GusDecodingTables.GusOffset.MidiData:
                return DecodeMidiData(port, value, width, direction);
            case GusDecodingTables.GusOffset.Gf1Page:
                return DecodeGf1Page(port, value, width, direction);
            case GusDecodingTables.GusOffset.Gf1RegisterSelect:
                return DecodeGf1RegisterSelect(port, value, width, direction);
            case GusDecodingTables.GusOffset.Gf1DataLow:
                return DecodeGf1DataLow(port, value, width, direction);
            case GusDecodingTables.GusOffset.Gf1DataHigh:
                return DecodeGf1DataHigh(port, value, width, direction);
            case GusDecodingTables.GusOffset.DramAddressLow:
                return DecodeDramAddressLow(port, value, width, direction);
            case GusDecodingTables.GusOffset.DramAddressHigh:
                return DecodeDramAddressHigh(port, value, width, direction);
        }
        return Build("Unknown GUS Port",
            "No specific decoder for this port.",
            port, value, width, direction,
            [GusPortParameters.RawByte("value", port, value)]);
    }

    private static DecodedCall DecodeMixControl(ushort port, uint value, int width, IoPortAccessDirection direction) {
        return Build(
            $"GUS Mix Control (0x{port:X3})",
            "Mix Control Register (write only): bit 0 = line-in enable (0 = enable), bit 1 = line-out enable (0 = enable), bit 2 = MIC input enable, bit 3 = latch reset, bit 5 = enable IRQ latches, bit 6 = enable DMA channel-1 latch.",
            port, value, width, direction,
            [GusPortParameters.RawByte("mix_control", port, value)]);
    }

    private static DecodedCall DecodeIrqStatus(ushort port, uint value, int width, IoPortAccessDirection direction) {
        return Build(
            $"GUS IRQ Status (0x{port:X3})",
            "IRQ Status Register (read only): bit 0 = MIDI transmit IRQ, bit 1 = MIDI receive IRQ, bit 2 = Timer 1 IRQ, bit 3 = Timer 2 IRQ, bit 5 = wave-table IRQ, bit 6 = volume-ramp IRQ, bit 7 = DMA TC IRQ.",
            port, value, width, direction,
            [GusPortParameters.RawByte("status", port, value)]);
    }

    private static DecodedCall DecodeAdlibTimerCommand(ushort port, uint value, int width, IoPortAccessDirection direction) {
        return Build(
            $"GUS AdLib-compat Timer Command (0x{port:X3})",
            "AdLib OPL2 timer compatibility register; software writes 0x04 to reset the timer interrupt flags.",
            port, value, width, direction,
            [GusPortParameters.RawByte("timer_command", port, value)]);
    }

    private static DecodedCall DecodeAdlibTimerData(ushort port, uint value, int width, IoPortAccessDirection direction) {
        return Build(
            $"GUS AdLib-compat Timer Data (0x{port:X3})",
            "AdLib OPL2 timer compatibility data port; reads return the timer status byte (bit 7 = IRQ, bits 5/6 = Timer 1/2 expired).",
            port, value, width, direction,
            [GusPortParameters.RawByte("timer_data", port, value)]);
    }

    private static DecodedCall DecodeAdlibCommand(ushort port, uint value, int width, IoPortAccessDirection direction) {
        return Build(
            $"GUS AdLib-compat Command (0x{port:X3})",
            "AdLib OPL2 command-port mirror; on a real GUS this is forwarded to the SBOS / MegaEm AdLib emulation layer.",
            port, value, width, direction,
            [GusPortParameters.RawByte("adlib_command", port, value)]);
    }

    private static DecodedCall DecodeIrqDmaControlSet(ushort port, uint value, int width, IoPortAccessDirection direction) {
        return Build(
            $"GUS IRQ/DMA Control Set (0x{port:X3})",
            "Sets the IRQ-channel or DMA-channel latch selected by the previous write to 2XF (Register Controls Select). Encodes IRQ/DMA channel numbers in the low nibble.",
            port, value, width, direction,
            [GusPortParameters.RawByte("irq_dma_value", port, value)]);
    }

    private static DecodedCall DecodeRegisterControlsSelect(ushort port, uint value, int width, IoPortAccessDirection direction) {
        byte raw = (byte)(value & 0xFF);
        string mnemonic;
        // Bits 6:5 select which latch the next write to 2XB sets:
        //   00 = control / mix-control reset, 40h = IRQ-channel latch, 80h = DMA-channel latch, C0h = Combined.
        switch (raw & 0xC0) {
            case 0x40:
                mnemonic = "select IRQ-channel latch";
                break;
            case 0x80:
                mnemonic = "select DMA-channel latch";
                break;
            case 0xC0:
                mnemonic = "select combined IRQ+DMA latch";
                break;
            default:
                mnemonic = "control / reset";
                break;
        }
        return Build(
            $"GUS Register Controls Select (0x{port:X3})",
            "Selects which IRQ/DMA latch is targeted by the next write to 2XB.",
            port, value, width, direction,
            [GusPortParameters.ByteWithName("select", port, raw, mnemonic)]);
    }

    private static DecodedCall DecodeMidiControl(ushort port, uint value, int width, IoPortAccessDirection direction) {
        return Build(
            $"GUS MIDI Control (0x{port:X3})",
            "MPU-401-compatible MIDI UART control register: writes are commands (0xFF = master reset, 0x03 = enable receive, 0x07 = enable transmit and receive); reads return UART status bits.",
            port, value, width, direction,
            [GusPortParameters.RawByte("midi_control", port, value)]);
    }

    private static DecodedCall DecodeMidiData(ushort port, uint value, int width, IoPortAccessDirection direction) {
        return Build(
            $"GUS MIDI Data (0x{port:X3})",
            "MPU-401-compatible MIDI UART data port: writes send a MIDI byte to the GF1 MIDI output, reads pull the next received MIDI byte.",
            port, value, width, direction,
            [GusPortParameters.RawByte("midi_byte", port, value)]);
    }

    private static DecodedCall DecodeGf1Page(ushort port, uint value, int width, IoPortAccessDirection direction) {
        byte raw = (byte)(value & 0x1F);
        return Build(
            $"GUS GF1 Page Register (0x{port:X3})",
            "Selects the active voice (0..31) for subsequent per-voice register accesses through 3X3 / 3X4 / 3X5.",
            port, value, width, direction,
            [GusPortParameters.ByteWithName("voice", port, raw, $"voice {raw}")]);
    }

    private static DecodedCall DecodeGf1RegisterSelect(ushort port, uint value, int width, IoPortAccessDirection direction) {
        byte raw = (byte)(value & 0xFF);
        string regName = GusDecodingTables.LookupGf1Register(raw);
        return Build(
            $"GUS GF1 Register Select (0x{port:X3})",
            "Selects which GF1 register is read/written through the data port (3X4 / 3X5). Per-voice registers (0x00..0x0F and read-mirrors 0x80..0x8F) refer to the voice currently latched in 3X2; control-block registers (0x40..0x4F) are global.",
            port, value, width, direction,
            [GusPortParameters.ByteWithName("register", port, raw, regName)]);
    }

    private static DecodedCall DecodeGf1DataLow(ushort port, uint value, int width, IoPortAccessDirection direction) {
        IReadOnlyList<DecodedParameter> parameters;
        if (width >= 2) {
            parameters = [GusPortParameters.RawWord("data16", port, value, "16-bit data window: low byte at 3X4, high byte at 3X5")];
        } else {
            parameters = [GusPortParameters.RawByte("data_low", port, value, "low byte of the 16-bit GF1 data window")];
        }
        return Build(
            $"GUS GF1 Data Port Low (0x{port:X3})",
            "Reads/writes the low byte of the 16-bit GF1 data window. The target register is the one most recently selected via 3X3 (and, for per-voice registers, the voice latched in 3X2).",
            port, value, width, direction,
            parameters);
    }

    private static DecodedCall DecodeGf1DataHigh(ushort port, uint value, int width, IoPortAccessDirection direction) {
        return Build(
            $"GUS GF1 Data Port High (0x{port:X3})",
            "Reads/writes the high byte of the 16-bit GF1 data window. Some GF1 registers are 8-bit and use only this byte (e.g. Voice Control, Pan Position).",
            port, value, width, direction,
            [GusPortParameters.RawByte("data_high", port, value)]);
    }

    private static DecodedCall DecodeDramAddressLow(ushort port, uint value, int width, IoPortAccessDirection direction) {
        return Build(
            $"GUS DRAM I/O Address Low (0x{port:X3})",
            "Low 8 bits of the on-card DRAM address used for sample upload/download via the DMA-less DRAM I/O path. Combines with 3X7 (high byte) and the GF1 Register 0x44 high nibble to form the full DRAM address.",
            port, value, width, direction,
            [GusPortParameters.RawByte("dram_addr_lo", port, value)]);
    }

    private static DecodedCall DecodeDramAddressHigh(ushort port, uint value, int width, IoPortAccessDirection direction) {
        return Build(
            $"GUS DRAM I/O Data / Address High (0x{port:X3})",
            "On a real GUS this port carries the high byte of the DRAM I/O address as well as the data byte read/written from the on-card DRAM at the configured address.",
            port, value, width, direction,
            [GusPortParameters.RawByte("dram_addr_hi", port, value)]);
    }

    private static DecodedCall Build(
        string functionName,
        string shortDescription,
        ushort port,
        uint value,
        int width,
        IoPortAccessDirection direction,
        IReadOnlyList<DecodedParameter> parameters) {
        string verb;
        if (direction == IoPortAccessDirection.Read) {
            verb = "in";
        } else {
            verb = "out";
        }
        string desc = $"{verb} 0x{port:X3} (width={width}): {shortDescription}";
        _ = value;
        return new DecodedCall(Subsystem, functionName, desc, parameters, []);
    }
}
