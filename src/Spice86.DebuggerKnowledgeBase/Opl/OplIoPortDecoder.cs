namespace Spice86.DebuggerKnowledgeBase.Opl;

using System.Collections.Generic;

using Spice86.DebuggerKnowledgeBase.Decoding;

/// <summary>
/// Decodes OPL FM synthesizer I/O port reads and writes for every OPL flavour Spice86
/// supports: OPL2 (single Yamaha YM3812), Dual OPL2 (SB Pro 1 stereo split), OPL3
/// (Yamaha YMF262, dual register array), and OPL3 Gold (OPL3 plus the AdLib Gold
/// signal-path: stereo processor, surround/YM7128B module).
/// </summary>
/// <remarks>
/// <para>
/// The "real" AdLib base ports are 0x388 (Status read / Address write) and 0x389
/// (Data write). OPL3 adds a second register array reachable through 0x38A
/// (Address-2 write) and 0x38B (Data-2 write). The same 0x38A / 0x38B pair is
/// reused by AdLib Gold to select and write the "control unit" registers (master
/// volume, bass, treble, switch functions, FM volumes, surround control) once the
/// control unit has been activated by writing <c>0xFF</c> to 0x38A; <c>0xFE</c>
/// deactivates it. Mirrors the dispatch in dosbox-staging's <c>opl.cpp</c>,
/// <c>adlib_gold.cpp</c>, and the Spice86 <c>Opl3Fm.PortWrite</c> switch.
/// </para>
/// <para>
/// Mode-specific notes (the decoder is pure / read-only and does not know which
/// mode is currently selected, so it documents both possibilities where they
/// diverge):
/// <list type="bullet">
///   <item><description>OPL2 — only 0x388 / 0x389 are meaningful; 0x38A / 0x38B
///   alias to the same chip on real hardware.</description></item>
///   <item><description>Dual OPL2 — the second YM3812 lives at SB-base + 2 / + 3
///   (handled by the Sound Blaster decoder, not here).</description></item>
///   <item><description>OPL3 — 0x38A / 0x38B address the secondary register
///   array (registers 0x100..0x1F5).</description></item>
///   <item><description>OPL3 Gold — 0x38A / 0x38B carry both the OPL3 secondary
///   array and the AdLib Gold control unit traffic, multiplexed by the
///   activation byte (<c>0xFF</c> to enter, <c>0xFE</c> to exit).</description></item>
/// </list>
/// </para>
/// <para>
/// The Sound Blaster card mirrors 0x388 / 0x389 at SB-base + 8 / + 9 (and SB Pro
/// stereo also at SB-base + 0 / + 1 / + 2 / + 3). Those mirrors are claimed by the
/// Sound Blaster decoder so that the SB knowledge base stays self-contained;
/// this OPL decoder owns only the dedicated AdLib port range 0x388..0x38B.
/// </para>
/// </remarks>
public sealed class OplIoPortDecoder : IIoPortDecoder {
    private const string Subsystem = "OPL FM I/O Ports";

    private const ushort PortStatusOrAddress0 = 0x388;
    private const ushort PortData0 = 0x389;
    private const ushort PortStatusOrAddress1 = 0x38A;
    private const ushort PortData1 = 0x38B;

    private const byte AdlibGoldActivate = 0xFF;
    private const byte AdlibGoldDeactivate = 0xFE;

    /// <inheritdoc />
    public bool CanDecode(ushort port) {
        return port >= PortStatusOrAddress0 && port <= PortData1;
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
        switch (port) {
            case PortStatusOrAddress0:
                return DecodeStatusOrAddress(port, value, width, direction, arrayIndex: 0);
            case PortData0:
                return DecodeData(port, value, width, direction, arrayIndex: 0);
            case PortStatusOrAddress1:
                return DecodeStatusOrAddress(port, value, width, direction, arrayIndex: 1);
            case PortData1:
                return DecodeData(port, value, width, direction, arrayIndex: 1);
            default:
                return Build("Unknown OPL Port",
                    "No specific decoder for this port.",
                    port, value, width, direction,
                    [OplPortParameters.RawByte("value", port, value)]);
        }
    }

    private static DecodedCall DecodeStatusOrAddress(ushort port, uint value, int width, IoPortAccessDirection direction, int arrayIndex) {
        if (direction == IoPortAccessDirection.Read) {
            string name;
            string description;
            if (arrayIndex == 0) {
                name = $"OPL FM Status Read (0x{port:X3})";
                description = "Returns the OPL status byte: bit 7 = IRQ pending (timer expired), bit 6 = Timer 1 expired, bit 5 = Timer 2 expired. SB Pro / SB16 force the lower bits to a chip-identification pattern (0x06).";
            } else {
                name = $"OPL3 Array-1 Status Read (0x{port:X3})";
                description = "On real hardware OPL3 mirrors the status byte at 0x38A as well. AdLib Gold can override this to return the control-unit state.";
            }
            return Build(name, description, port, value, width, direction,
                [OplPortParameters.RawByte("status", port, value)]);
        }

        // Write path: register-address selection, with the AdLib Gold activate/deactivate side-channel on 0x38A.
        byte raw = (byte)(value & 0xFF);
        if (arrayIndex == 1) {
            if (raw == AdlibGoldActivate) {
                return Build(
                    $"AdLib Gold Control Activate (0x{port:X3})",
                    "Writing 0xFF to 0x38A enters AdLib Gold control mode: subsequent writes to 0x38A select an AdLib Gold control register, and writes to 0x38B carry the control data. Only meaningful when the OPL is configured as OPL3 Gold.",
                    port, value, width, direction,
                    [OplPortParameters.ByteWithName("control", port, raw, "Activate AdLib Gold control unit")]);
            }
            if (raw == AdlibGoldDeactivate) {
                return Build(
                    $"AdLib Gold Control Deactivate (0x{port:X3})",
                    "Writing 0xFE to 0x38A exits AdLib Gold control mode and returns the port to its OPL3 secondary-array role.",
                    port, value, width, direction,
                    [OplPortParameters.ByteWithName("control", port, raw, "Deactivate AdLib Gold control unit")]);
            }

            // Either: AdLib Gold control index (when activated), or OPL3 Array-1 register address.
            string goldName = OplDecodingTables.Lookup(OplDecodingTables.AdlibGoldControls, raw);
            string regName = OplDecodingTables.LookupRegister((ushort)(0x100 | raw));
            return Build(
                $"OPL3 Array-1 Address / AdLib Gold Control Index Write (0x{port:X3})",
                "Mode-dependent: in OPL3 mode this selects an Array-1 OPL register (0x100..0x1F5); in OPL3 Gold mode after activation (0xFF) it selects an AdLib Gold control register (volume / bass / treble / switch / FM volumes / surround).",
                port, value, width, direction,
                [
                    OplPortParameters.ByteWithName("opl3_register", port, raw, regName, "OPL3 / OPL3 Gold mode"),
                    OplPortParameters.ByteWithName("gold_control", port, raw, goldName, "AdLib Gold control mode (after 0xFF activation)")
                ]);
        }

        // Array 0 — straightforward OPL register address write (0..0xF5 valid on OPL2; 0..0xFF accepted by OPL3).
        string array0Name = OplDecodingTables.LookupRegister(raw);
        return Build(
            $"OPL FM Register Address Write (0x{port:X3})",
            "Selects the OPL register addressed by subsequent writes to the data port (0x389). Valid range is 0x00..0xF5 on OPL2 and 0x00..0xFF on OPL3 (Array 0).",
            port, value, width, direction,
            [OplPortParameters.ByteWithName("register", port, raw, array0Name)]);
    }

    private static DecodedCall DecodeData(ushort port, uint value, int width, IoPortAccessDirection direction, int arrayIndex) {
        if (direction == IoPortAccessDirection.Read) {
            string readName;
            string readDescription;
            if (arrayIndex == 0) {
                readName = $"OPL FM Data Read (0x{port:X3})";
                readDescription = "Reads typically return 0xFF on real hardware; data is normally only written.";
            } else {
                readName = $"OPL3 Array-1 Data Read / AdLib Gold Control Read (0x{port:X3})";
                readDescription = "In OPL3 Gold mode this exposes the AdLib Gold control unit state (e.g. surround / stereo control snapshot) as documented in dosbox-staging's adlib_gold.cpp.";
            }
            return Build(readName, readDescription, port, value, width, direction,
                [OplPortParameters.RawByte("value", port, value)]);
        }

        string name;
        string description;
        if (arrayIndex == 0) {
            name = $"OPL FM Data Write (0x{port:X3})";
            description = "Writes the byte to the OPL register most recently selected via 0x388. The byte's meaning depends on that register (see OplDecodingTables.LookupRegister).";
        } else {
            name = $"OPL3 Array-1 Data / AdLib Gold Control Data Write (0x{port:X3})";
            description = "Mode-dependent: in OPL3 mode this writes to the Array-1 register selected via 0x38A; in OPL3 Gold mode after activation it writes a control-unit value (volume level, bass / treble / switch-functions byte, or one bit of the YM7128B surround serial stream).";
        }
        return Build(name, description, port, value, width, direction,
            [OplPortParameters.RawByte("value", port, value)]);
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
