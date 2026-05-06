namespace Spice86.DebuggerKnowledgeBase.Mpu401;

using System.Collections.Generic;

using Spice86.DebuggerKnowledgeBase.Decoding;

/// <summary>
/// Decodes MPU-401 I/O port reads and writes used by Roland General MIDI synths and the
/// Roland MT-32 (which expose the same MPU-401 register interface; only the MIDI payload
/// semantics differ).
/// </summary>
/// <remarks>
/// <para>
/// The MPU-401 occupies a 2-port window starting at the configurable base address:
/// </para>
/// <list type="bullet">
///   <item><description><c>base + 0</c> — Data port. Reads dequeue the next byte from the
///   MPU-to-host FIFO; writes send a MIDI byte (UART mode) or a parameter byte for the
///   most recently issued intelligent-mode command.</description></item>
///   <item><description><c>base + 1</c> — Status (read) / Command (write). Status bit 7
///   = DSR (0 means a byte is available at <c>base + 0</c>); status bit 6 = DRR (0 means
///   the MPU is ready to receive another command/data byte). Writes to the Command port
///   issue MPU-401 commands such as <c>0x3F</c> "Enter UART mode" or <c>0xFF</c>
///   "Reset".</description></item>
/// </list>
/// <para>
/// Standard base addresses are 0x300, 0x310, 0x320, 0x330 (default), 0x332, 0x334,
/// 0x336, 0x338, 0x340, 0x350, 0x360. Spice86 itself only registers ports 0x330 / 0x331
/// (see <c>Spice86.Core.Emulator.Devices.Sound.Midi.Midi</c>), but the decoder claims
/// the wider set so it remains useful when the user reconfigures the card or when an
/// alternative MPU base is observed in traces.
/// </para>
/// <para>
/// Mirrors the dispatch performed by dosbox-staging's <c>mpu401.cpp</c> (in particular
/// <c>MPU401_WriteCommand</c> and <c>MPU401_WriteData</c>). The decoder is pure and
/// read-only: parameter bytes following a multi-byte intelligent-mode command (e.g.
/// <c>0xE0</c> "Set tempo") cannot be associated with their command without runtime
/// state, so they are described as plain MIDI/data bytes and the caller is responsible
/// for keeping a small window of recent writes if the full picture is needed.
/// </para>
/// </remarks>
public sealed class Mpu401IoPortDecoder : IIoPortDecoder {
    private const string Subsystem = "MPU-401 (General MIDI / MT-32)";

    /// <summary>Standard MPU-401 base addresses commonly reported by ISA cards.</summary>
    internal static readonly IReadOnlyList<ushort> StandardBaseAddresses = new ushort[] {
        0x300, 0x310, 0x320, 0x330, 0x332, 0x334, 0x336, 0x338, 0x340, 0x350, 0x360
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
            return Build("Unknown MPU-401 Port",
                "No specific decoder for this port — falling back to the generic value.",
                port, value, width, direction,
                [Mpu401PortParameters.RawByte("value", port, value)]);
        }
        if (offset == 0) {
            return DataPort(port, baseAddress, value, width, direction);
        }
        return StatusOrCommandPort(port, baseAddress, value, width, direction);
    }

    private static DecodedCall DataPort(ushort port, ushort baseAddress, uint value, int width, IoPortAccessDirection direction) {
        byte raw = (byte)(value & 0xFF);
        if (direction == IoPortAccessDirection.Write) {
            string mnemonic = Mpu401DecodingTables.DescribeMidiByte(raw);
            return Build(
                "MPU-401 Data Write (base + 0)",
                "Sends a MIDI byte to the synth (UART mode) or a parameter byte for the most recently issued intelligent-mode command. Roland General MIDI and MT-32 share this interface; the MIDI payload bytes are interpreted by the connected synth.",
                port, value, width, direction,
                [Mpu401PortParameters.ByteWithName("midi byte", port, raw, mnemonic, $"base=0x{baseAddress:X3}")]);
        }
        return Build(
            "MPU-401 Data Read (base + 0)",
            "Dequeues the next byte from the MPU-to-host FIFO (e.g. MPU command ACK 0xFE, version/revision response, intelligent-mode track data, or an incoming MIDI byte forwarded by the synth). Returns 0 when the FIFO is empty.",
            port, value, width, direction,
            [Mpu401PortParameters.RawByte("value", port, value, $"base=0x{baseAddress:X3}")]);
    }

    private static DecodedCall StatusOrCommandPort(ushort port, ushort baseAddress, uint value, int width, IoPortAccessDirection direction) {
        if (direction == IoPortAccessDirection.Write) {
            byte command = (byte)(value & 0xFF);
            string mnemonic = Mpu401DecodingTables.Lookup(Mpu401DecodingTables.Commands, command);
            return Build(
                "MPU-401 Command Write (base + 1)",
                "Issues an MPU-401 command. Software typically writes 0x3F to enter UART mode (the only mode used by virtually all DOS General MIDI / MT-32 software) or 0xFF to reset the MPU. The MPU acknowledges most commands by placing 0xFE on the read FIFO at base + 0.",
                port, value, width, direction,
                [Mpu401PortParameters.ByteWithName("command", port, command, mnemonic, $"base=0x{baseAddress:X3}")]);
        }
        return Build(
            "MPU-401 Status Read (base + 1)",
            "Bit 7 (DSR) = 0 when a byte is available at base + 0; bit 6 (DRR) = 0 when the MPU is ready to accept another command/data byte. Lower bits are unused.",
            port, value, width, direction,
            [Mpu401PortParameters.RawByte("status", port, value, $"base=0x{baseAddress:X3}, bit7=DSR (0=output ready), bit6=DRR (0=ready to receive)")]);
    }

    private static bool TryGetBase(ushort port, out ushort baseAddress, out int offset) {
        foreach (ushort candidate in StandardBaseAddresses) {
            int delta = port - candidate;
            if (delta >= 0 && delta <= 1) {
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
