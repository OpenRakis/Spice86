namespace Spice86.DebuggerKnowledgeBase.Sb;

using System.Collections.Generic;

using Spice86.DebuggerKnowledgeBase.Decoding;

/// <summary>
/// Decodes Sound Blaster I/O port reads and writes for all SB variants
/// (SB1, SB2, SB Pro 1, SB Pro 2, SB16, Game Blaster / CMS, plus ESS-flavoured SB Pro 2).
/// </summary>
/// <remarks>
/// <para>
/// The SB chip occupies a 16-port window starting at the configurable base address (one of
/// 0x210, 0x220, 0x230, 0x240, 0x250, 0x260, 0x280). This decoder claims every offset in that
/// window across all standard bases so it works regardless of how the user configured the card.
/// </para>
/// <para>
/// Mirrors the dispatch performed by dosbox-staging's <c>soundblaster.cpp</c> and by
/// <see cref="Spice86.Core.Emulator.Devices.Sound.Blaster.SoundBlaster"/>'s
/// <c>ReadByte</c>/<c>WriteByte</c> switch on <c>SoundBlasterPortOffset</c>.
/// </para>
/// <para>
/// The DSP ports are stateful (the FIFO buffers and the multi-byte command parameter machine
/// live in emulator state, which decoders must not touch). We expose the command-byte mnemonic
/// for writes to the DSP write port (offset 0x0C) but cannot interpret subsequent parameter
/// bytes; callers that need the full picture must keep a small window of recent writes. The
/// same applies to the mixer data port (offset 0x05), where the meaning of the byte depends on
/// the most recently written mixer index (offset 0x04).
/// </para>
/// </remarks>
public sealed class SbIoPortDecoder : IIoPortDecoder {
    private const string Subsystem = "Sound Blaster I/O Ports";

    /// <summary>Standard Creative-documented Sound Blaster base addresses.</summary>
    internal static readonly IReadOnlyList<ushort> StandardBaseAddresses = new ushort[] {
        0x210, 0x220, 0x230, 0x240, 0x250, 0x260, 0x280
    };

    /// <inheritdoc />
    public bool CanDecode(ushort port) {
        return TryGetBase(port, out _, out _);
    }

    /// <inheritdoc />
    public DecodedCall DecodeRead(ushort port, uint value, int width) {
        return Decode(port, value, width, IoPortAccessDirection.Read);
    }

    /// <inheritdoc />
    public DecodedCall DecodeWrite(ushort port, uint value, int width) {
        return Decode(port, value, width, IoPortAccessDirection.Write);
    }

    private static DecodedCall Decode(ushort port, uint value, int width, IoPortAccessDirection direction) {
        if (!TryGetBase(port, out ushort baseAddress, out int offset)) {
            return Build("Unknown SB Port",
                "No specific decoder for this port — falling back to the generic value.",
                port, value, width, direction,
                [SbPortParameters.RawByte("value", port, value)]);
        }
        switch (offset) {
            case 0x00:
            case 0x01:
            case 0x02:
            case 0x03:
                return AdlibLeftRight(port, baseAddress, offset, value, width, direction);
            case 0x04:
                return MixerIndex(port, baseAddress, value, width, direction);
            case 0x05:
                return MixerData(port, baseAddress, value, width, direction);
            case 0x06:
                return DspReset(port, baseAddress, value, width, direction);
            case 0x07:
                return Reserved(port, baseAddress, offset, value, width, direction);
            case 0x08:
            case 0x09:
                return OplPassthrough(port, baseAddress, offset, value, width, direction);
            case 0x0A:
                return DspReadData(port, baseAddress, value, width, direction);
            case 0x0B:
                return Reserved(port, baseAddress, offset, value, width, direction);
            case 0x0C:
                return DspWriteOrStatus(port, baseAddress, value, width, direction);
            case 0x0D:
                return Reserved(port, baseAddress, offset, value, width, direction);
            case 0x0E:
                return DspReadStatus(port, baseAddress, value, width, direction);
            case 0x0F:
                return DspAck16Bit(port, baseAddress, value, width, direction);
            default:
                return Build("Unknown SB Port",
                    "No specific decoder for this offset.",
                    port, value, width, direction,
                    [SbPortParameters.RawByte("value", port, value)]);
        }
    }

    private static DecodedCall AdlibLeftRight(ushort port, ushort baseAddress, int offset, uint value, int width, IoPortAccessDirection direction) {
        string side;
        if (offset == 0x00 || offset == 0x01) {
            side = "left";
        } else {
            side = "right";
        }
        string role;
        if ((offset & 0x01) == 0) {
            role = "register/index";
        } else {
            role = "data";
        }
        return Build(
            $"AdLib FM {side} {role} (base + 0x{offset:X2})",
            "Adlib-compatible OPL2 register window: SB Pro stereo splits the FM chip in two so left/right pairs land here. Game Blaster (CMS) also lives here. SB1/SB2/SB16 do not normally claim this offset.",
            port, value, width, direction,
            [
                SbPortParameters.RawByte("value", port, value, $"base=0x{baseAddress:X3}, offset=0x{offset:X2}")
            ]);
    }

    private static DecodedCall MixerIndex(ushort port, ushort baseAddress, uint value, int width, IoPortAccessDirection direction) {
        if (direction == IoPortAccessDirection.Write) {
            byte index = (byte)(value & 0xFF);
            string name = SbDecodingTables.Lookup(SbDecodingTables.MixerRegisters, index);
            return Build(
                "Mixer Index Write (base + 0x04)",
                "Selects the SB mixer register addressed by subsequent reads/writes to base + 0x05. Not present on SB1 / SB2.",
                port, value, width, direction,
                [SbPortParameters.IndexWithName("mixer index", port, index, name, $"base=0x{baseAddress:X3}")]);
        }
        return Build(
            "Mixer Index Read (base + 0x04)",
            "Returns the currently selected SB mixer register index.",
            port, value, width, direction,
            [SbPortParameters.RawByte("index", port, value, $"base=0x{baseAddress:X3}")]);
    }

    private static DecodedCall MixerData(ushort port, ushort baseAddress, uint value, int width, IoPortAccessDirection direction) {
        string name;
        string description;
        if (direction == IoPortAccessDirection.Write) {
            name = "Mixer Data Write (base + 0x05)";
            description = "Writes the byte to the SB mixer register selected via base + 0x04. The byte's meaning depends on that index (see SbDecodingTables.MixerRegisters).";
        } else {
            name = "Mixer Data Read (base + 0x05)";
            description = "Reads the SB mixer register selected via base + 0x04.";
        }
        return Build(name, description, port, value, width, direction,
            [SbPortParameters.RawByte("value", port, value, $"base=0x{baseAddress:X3}")]);
    }

    private static DecodedCall DspReset(ushort port, ushort baseAddress, uint value, int width, IoPortAccessDirection direction) {
        if (direction == IoPortAccessDirection.Write) {
            byte raw = (byte)(value & 0xFF);
            string note;
            if (raw == 0x01) {
                note = "reset asserted (write 0x01 then 0x00 to reset DSP)";
            } else if (raw == 0x00) {
                note = "reset deasserted";
            } else {
                note = "non-standard reset value";
            }
            return Build(
                "DSP Reset Write (base + 0x06)",
                "Resets the DSP. Software writes 0x01 here, waits at least 3 microseconds, then writes 0x00. The DSP signals reset success by placing 0xAA in the read FIFO.",
                port, value, width, direction,
                [SbPortParameters.RawByte("reset", port, value, $"base=0x{baseAddress:X3}, {note}")]);
        }
        return Build(
            "DSP Reset Read (base + 0x06)",
            "Reads always return 0xFF; this register has no read semantics.",
            port, value, width, direction,
            [SbPortParameters.RawByte("value", port, value, $"base=0x{baseAddress:X3}")]);
    }

    private static DecodedCall DspReadData(ushort port, ushort baseAddress, uint value, int width, IoPortAccessDirection direction) {
        if (direction == IoPortAccessDirection.Read) {
            return Build(
                "DSP Read Data (base + 0x0A)",
                "Reads the next byte from the DSP output FIFO (e.g. DSP version low/high byte, identification echo, copyright string byte).",
                port, value, width, direction,
                [SbPortParameters.RawByte("value", port, value, $"base=0x{baseAddress:X3}")]);
        }
        return Build(
            "DSP Read Data Write (base + 0x0A)",
            "Writes here are ignored; the read FIFO is read-only.",
            port, value, width, direction,
            [SbPortParameters.RawByte("value", port, value, $"base=0x{baseAddress:X3}")]);
    }

    private static DecodedCall DspWriteOrStatus(ushort port, ushort baseAddress, uint value, int width, IoPortAccessDirection direction) {
        if (direction == IoPortAccessDirection.Write) {
            byte command = (byte)(value & 0xFF);
            string name = SbDecodingTables.Lookup(SbDecodingTables.DspCommands, command);
            return Build(
                "DSP Write Command/Data (base + 0x0C)",
                "Writes a DSP command byte or a parameter byte for an in-progress command. The first byte of a command sequence selects the operation; subsequent bytes (count depends on the command) carry parameters.",
                port, value, width, direction,
                [SbPortParameters.IndexWithName("byte", port, command, name, $"base=0x{baseAddress:X3}")]);
        }
        return Build(
            "DSP Write Buffer Status (base + 0x0C)",
            "Reports whether the DSP write FIFO is ready to accept another byte (bit 7 = 1 means buffer at capacity). Lower 7 bits are always 1.",
            port, value, width, direction,
            [SbPortParameters.RawByte("status", port, value, $"base=0x{baseAddress:X3}, bit 7 = full/busy")]);
    }

    private static DecodedCall DspReadStatus(ushort port, ushort baseAddress, uint value, int width, IoPortAccessDirection direction) {
        if (direction == IoPortAccessDirection.Read) {
            return Build(
                "DSP Read Buffer Status / 8-bit IRQ Ack (base + 0x0E)",
                "Bit 7 = 1 when the DSP read FIFO has a byte available. Reading this port also acknowledges the 8-bit playback IRQ. Lower 7 bits are always 1.",
                port, value, width, direction,
                [SbPortParameters.RawByte("status", port, value, $"base=0x{baseAddress:X3}, bit 7 = data available; read also acks 8-bit IRQ")]);
        }
        return Build(
            "DSP Read Buffer Status Write (base + 0x0E)",
            "Writes here are ignored.",
            port, value, width, direction,
            [SbPortParameters.RawByte("value", port, value, $"base=0x{baseAddress:X3}")]);
    }

    private static DecodedCall DspAck16Bit(ushort port, ushort baseAddress, uint value, int width, IoPortAccessDirection direction) {
        if (direction == IoPortAccessDirection.Read) {
            return Build(
                "DSP 16-bit IRQ Ack (base + 0x0F)",
                "Reading this port acknowledges the 16-bit playback IRQ on SB16. Always returns 0xFF.",
                port, value, width, direction,
                [SbPortParameters.RawByte("value", port, value, $"base=0x{baseAddress:X3}, ack 16-bit IRQ (SB16)")]);
        }
        return Build(
            "DSP 16-bit IRQ Ack Write (base + 0x0F)",
            "Writes here are ignored.",
            port, value, width, direction,
            [SbPortParameters.RawByte("value", port, value, $"base=0x{baseAddress:X3}")]);
    }

    private static DecodedCall OplPassthrough(ushort port, ushort baseAddress, int offset, uint value, int width, IoPortAccessDirection direction) {
        return Build(
            $"OPL FM at SB Window (base + 0x{offset:X2})",
            "On real hardware, base + 0x08 / 0x09 mirror the AdLib FM register/data pair (0x388 / 0x389). The Spice86 SB port registration deliberately skips these so the OPL device handles them directly.",
            port, value, width, direction,
            [SbPortParameters.RawByte("value", port, value, $"base=0x{baseAddress:X3}, offset=0x{offset:X2}")]);
    }

    private static DecodedCall Reserved(ushort port, ushort baseAddress, int offset, uint value, int width, IoPortAccessDirection direction) {
        return Build(
            $"Reserved SB Port (base + 0x{offset:X2})",
            "Reserved / unused SB port offset. Reads typically return 0xFF; writes are ignored.",
            port, value, width, direction,
            [SbPortParameters.RawByte("value", port, value, $"base=0x{baseAddress:X3}, offset=0x{offset:X2}")]);
    }

    private static bool TryGetBase(ushort port, out ushort baseAddress, out int offset) {
        foreach (ushort candidate in StandardBaseAddresses) {
            int delta = port - candidate;
            if (delta >= 0 && delta <= 0x0F) {
                baseAddress = candidate;
                offset = delta;
                return true;
            }
        }
        baseAddress = 0;
        offset = -1;
        return false;
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
