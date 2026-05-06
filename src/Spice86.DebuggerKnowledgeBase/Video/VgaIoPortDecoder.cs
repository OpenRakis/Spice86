namespace Spice86.DebuggerKnowledgeBase.Video;

using System.Collections.Generic;

using Spice86.DebuggerKnowledgeBase.Decoding;

/// <summary>
/// Decodes VGA I/O port reads and writes (ports 0x3B4..0x3BA monochrome group, 0x3C0..0x3CF
/// Attribute / Misc / Sequencer / DAC / Graphics, and 0x3D0..0x3DA color group).
/// </summary>
/// <remarks>
/// <para>
/// Mirrors the dispatch performed by dosbox-staging's <c>vga_misc.cpp</c>, <c>vga_attr.cpp</c>,
/// <c>vga_seq.cpp</c>, <c>vga_crtc.cpp</c>, <c>vga_gfx.cpp</c> and <c>vga_dac.cpp</c>. Indexed
/// register groups (Sequencer/CRTC/GC/AC) are decoded by name when the address port is being
/// written, since the index is the value being written. The data ports are described generically
/// because the currently selected index is part of emulator state (which decoders must not touch).
/// </para>
/// <para>
/// The Attribute Controller is special: port 0x3C0 alternates between an index write and a data
/// write, gated by an internal flip-flop that is reset by reading port 0x3DA / 0x3BA. We expose a
/// single decoded entry that documents both halves of the protocol so the user can interpret a
/// 0x3C0 write in context of the most recent 0x3DA read.
/// </para>
/// </remarks>
public sealed class VgaIoPortDecoder : IIoPortDecoder {
    private const string Subsystem = "VGA I/O Ports";

    private static readonly HashSet<ushort> ClaimedPorts = new() {
        0x3B4, 0x3B5, 0x3BA,
        0x3C0, 0x3C1, 0x3C2, 0x3C3, 0x3C4, 0x3C5, 0x3C6, 0x3C7, 0x3C8, 0x3C9, 0x3CA, 0x3CC, 0x3CE, 0x3CF,
        0x3D0, 0x3D1, 0x3D2, 0x3D3, 0x3D4, 0x3D5, 0x3D8, 0x3D9, 0x3DA
    };

    /// <inheritdoc />
    public bool CanDecode(ushort port) {
        return ClaimedPorts.Contains(port);
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
        if (port == 0x3B4 || port == 0x3D4 || port == 0x3D0 || port == 0x3D2) {
            return CrtAddress(port, value, width, direction);
        }
        if (port == 0x3B5 || port == 0x3D5 || port == 0x3D1 || port == 0x3D3) {
            return CrtData(port, value, width, direction);
        }
        if (port == 0x3BA) {
            return MonoStatusOrFeatureControl(value, width, direction);
        }
        if (port == 0x3DA) {
            return ColorStatusOrFeatureControl(value, width, direction);
        }
        if (port == 0x3C0) {
            return AttributeAddressOrData(value, width, direction);
        }
        if (port == 0x3C1) {
            return AttributeReadData(value, width, direction);
        }
        if (port == 0x3C2) {
            return MiscOutputOrInputStatus0(value, width, direction);
        }
        if (port == 0x3C3) {
            return VideoSubsystemEnable(value, width, direction);
        }
        if (port == 0x3C4) {
            return SequencerAddress(value, width, direction);
        }
        if (port == 0x3C5) {
            return SequencerData(value, width, direction);
        }
        if (port == 0x3C6) {
            return DacPelMask(value, width, direction);
        }
        if (port == 0x3C7) {
            return DacReadIndexOrState(value, width, direction);
        }
        if (port == 0x3C8) {
            return DacWriteIndex(value, width, direction);
        }
        if (port == 0x3C9) {
            return DacData(value, width, direction);
        }
        if (port == 0x3CA) {
            return FeatureControlRead(value, width, direction);
        }
        if (port == 0x3CC) {
            return MiscOutputRead(value, width, direction);
        }
        if (port == 0x3CE) {
            return GraphicsControllerAddress(value, width, direction);
        }
        if (port == 0x3CF) {
            return GraphicsControllerData(value, width, direction);
        }
        if (port == 0x3D8) {
            return CgaModeControl(value, width, direction);
        }
        if (port == 0x3D9) {
            return CgaColorSelect(value, width, direction);
        }
        return Unknown(port, value, width, direction);
    }

    private static DecodedCall CrtAddress(ushort port, uint value, int width, IoPortAccessDirection direction) {
        string group = (port == 0x3B4 || port == 0x3B5) ? "monochrome" : "color";
        if (direction == IoPortAccessDirection.Write) {
            byte index = (byte)(value & 0xFF);
            string name = VgaIndexedRegisterTables.Lookup(VgaIndexedRegisterTables.CrtController, index);
            return Build(
                $"CRTC Address Write (0x{port:X3}, {group})",
                "Selects the CRT Controller register addressed by subsequent reads/writes to the data port.",
                port,
                value,
                width,
                direction,
                [VgaPortParameters.IndexWithName("CRTC index", port, index, name)]);
        }
        return Build(
            $"CRTC Address Read (0x{port:X3}, {group})",
            "Returns the currently selected CRT Controller index.",
            port,
            value,
            width,
            direction,
            [VgaPortParameters.RawByte("value", port, value)]);
    }

    private static DecodedCall CrtData(ushort port, uint value, int width, IoPortAccessDirection direction) {
        string group = (port == 0x3B4 || port == 0x3B5) ? "monochrome" : "color";
        string name = direction == IoPortAccessDirection.Write ? "CRTC Data Write" : "CRTC Data Read";
        string description = direction == IoPortAccessDirection.Write
            ? "Writes to the CRT Controller register selected via the matching address port."
            : "Reads the CRT Controller register selected via the matching address port.";
        return Build(
            $"{name} (0x{port:X3}, {group})",
            description,
            port,
            value,
            width,
            direction,
            [VgaPortParameters.RawByte("value", port, value)]);
    }

    private static DecodedCall MonoStatusOrFeatureControl(uint value, int width, IoPortAccessDirection direction) {
        if (direction == IoPortAccessDirection.Read) {
            return Build(
                "Input Status #1 Read (mono, 0x3BA)",
                "Reports vertical retrace (bit 3) and display-enable not (bit 0); reading also resets the Attribute Controller flip-flop.",
                0x3BA,
                value,
                width,
                direction,
                [VgaPortParameters.RawByte("status", 0x3BA, value)]);
        }
        return Build(
            "Feature Control Write (mono, 0x3BA)",
            "Writes feature-control bits (FC0/FC1 on EGA; reserved on VGA).",
            0x3BA,
            value,
            width,
            direction,
            [VgaPortParameters.RawByte("value", 0x3BA, value)]);
    }

    private static DecodedCall ColorStatusOrFeatureControl(uint value, int width, IoPortAccessDirection direction) {
        if (direction == IoPortAccessDirection.Read) {
            return Build(
                "Input Status #1 Read (color, 0x3DA)",
                "Reports vertical retrace (bit 3) and display-enable not (bit 0); reading also resets the Attribute Controller flip-flop.",
                0x3DA,
                value,
                width,
                direction,
                [VgaPortParameters.RawByte("status", 0x3DA, value)]);
        }
        return Build(
            "Feature Control Write (color, 0x3DA)",
            "Writes feature-control bits (FC0/FC1 on EGA; reserved on VGA).",
            0x3DA,
            value,
            width,
            direction,
            [VgaPortParameters.RawByte("value", 0x3DA, value)]);
    }

    private static DecodedCall AttributeAddressOrData(uint value, int width, IoPortAccessDirection direction) {
        if (direction == IoPortAccessDirection.Write) {
            byte raw = (byte)(value & 0xFF);
            byte index = (byte)(raw & 0x1F);
            bool paletteAddressSource = (raw & 0x20) != 0;
            string name = VgaIndexedRegisterTables.Lookup(VgaIndexedRegisterTables.AttributeController, index);
            string note = paletteAddressSource
                ? "PAS=1 (palette accesses enabled, normal display)"
                : "PAS=0 (palette accesses disabled, screen blanked)";
            return Build(
                "Attribute Controller Address-or-Data Write (0x3C0)",
                "First write after the flip-flop is the index byte (low 5 bits + PAS bit 5); next write is data for that register.",
                0x3C0,
                value,
                width,
                direction,
                [VgaPortParameters.IndexWithName("AC index/data", 0x3C0, raw, name, note)]);
        }
        return Build(
            "Attribute Controller Index Read (0x3C0)",
            "Returns the current Attribute Controller index byte (VGA only).",
            0x3C0,
            value,
            width,
            direction,
            [VgaPortParameters.RawByte("index", 0x3C0, value)]);
    }

    private static DecodedCall AttributeReadData(uint value, int width, IoPortAccessDirection direction) {
        if (direction == IoPortAccessDirection.Read) {
            return Build(
                "Attribute Controller Data Read (0x3C1)",
                "Reads the Attribute Controller register selected via 0x3C0.",
                0x3C1,
                value,
                width,
                direction,
                [VgaPortParameters.RawByte("value", 0x3C1, value)]);
        }
        return Build(
            "Attribute Controller Data Write (0x3C1)",
            "Undocumented write path; most BIOSes use 0x3C0 for both index and data.",
            0x3C1,
            value,
            width,
            direction,
            [VgaPortParameters.RawByte("value", 0x3C1, value)]);
    }

    private static DecodedCall MiscOutputOrInputStatus0(uint value, int width, IoPortAccessDirection direction) {
        if (direction == IoPortAccessDirection.Write) {
            byte raw = (byte)(value & 0xFF);
            string clock = ((raw >> 2) & 0x3) switch {
                0 => "25 MHz",
                1 => "28 MHz",
                _ => "external"
            };
            string ioMode = (raw & 0x01) != 0 ? "color (3Dx)" : "monochrome (3Bx)";
            return Build(
                "Misc Output Write (0x3C2)",
                "Selects clock, page select, I/O address mode, vertical/horizontal sync polarity.",
                0x3C2,
                value,
                width,
                direction,
                [
                    VgaPortParameters.RawByte("misc", 0x3C2, value, $"clock={clock}, IO={ioMode}, hsync_pol={((raw & 0x40) != 0 ? "neg" : "pos")}, vsync_pol={((raw & 0x80) != 0 ? "neg" : "pos")}")
                ]);
        }
        return Build(
            "Input Status #0 Read (0x3C2)",
            "Reports switch sense (bit 4) and CRT interrupt (bit 7, EGA only).",
            0x3C2,
            value,
            width,
            direction,
            [VgaPortParameters.RawByte("status", 0x3C2, value)]);
    }

    private static DecodedCall VideoSubsystemEnable(uint value, int width, IoPortAccessDirection direction) {
        return Build(
            "Video Subsystem Enable (0x3C3)",
            "VGA-only register that gates the entire video subsystem (bit 0 = enable).",
            0x3C3,
            value,
            width,
            direction,
            [VgaPortParameters.RawByte("value", 0x3C3, value)]);
    }

    private static DecodedCall SequencerAddress(uint value, int width, IoPortAccessDirection direction) {
        if (direction == IoPortAccessDirection.Write) {
            byte index = (byte)(value & 0xFF);
            string name = VgaIndexedRegisterTables.Lookup(VgaIndexedRegisterTables.Sequencer, index);
            return Build(
                "Sequencer Address Write (0x3C4)",
                "Selects the Sequencer register addressed by subsequent reads/writes to 0x3C5.",
                0x3C4,
                value,
                width,
                direction,
                [VgaPortParameters.IndexWithName("SR index", 0x3C4, index, name)]);
        }
        return Build(
            "Sequencer Address Read (0x3C4)",
            "Returns the currently selected Sequencer index.",
            0x3C4,
            value,
            width,
            direction,
            [VgaPortParameters.RawByte("value", 0x3C4, value)]);
    }

    private static DecodedCall SequencerData(uint value, int width, IoPortAccessDirection direction) {
        string name = direction == IoPortAccessDirection.Write ? "Sequencer Data Write (0x3C5)" : "Sequencer Data Read (0x3C5)";
        string description = direction == IoPortAccessDirection.Write
            ? "Writes to the Sequencer register selected via 0x3C4."
            : "Reads the Sequencer register selected via 0x3C4 (VGA only).";
        return Build(name, description, 0x3C5, value, width, direction,
            [VgaPortParameters.RawByte("value", 0x3C5, value)]);
    }

    private static DecodedCall DacPelMask(uint value, int width, IoPortAccessDirection direction) {
        return Build(
            "DAC Pixel Mask (0x3C6)",
            "ANDed with the pixel value before DAC lookup. Most code leaves this at 0xFF.",
            0x3C6,
            value,
            width,
            direction,
            [VgaPortParameters.RawByte("mask", 0x3C6, value)]);
    }

    private static DecodedCall DacReadIndexOrState(uint value, int width, IoPortAccessDirection direction) {
        if (direction == IoPortAccessDirection.Write) {
            return Build(
                "DAC Read Index Write (0x3C7)",
                "Sets the DAC entry index that subsequent reads from 0x3C9 will return (R, then G, then B).",
                0x3C7,
                value,
                width,
                direction,
                [VgaPortParameters.RawByte("dac index", 0x3C7, value)]);
        }
        return Build(
            "DAC State Read (0x3C7)",
            "Returns 0 when the DAC is in read mode, 3 when in write mode.",
            0x3C7,
            value,
            width,
            direction,
            [VgaPortParameters.RawByte("state", 0x3C7, value)]);
    }

    private static DecodedCall DacWriteIndex(uint value, int width, IoPortAccessDirection direction) {
        return Build(
            "DAC Write Index (0x3C8)",
            "Sets the DAC entry index that subsequent writes to 0x3C9 will populate (R, then G, then B).",
            0x3C8,
            value,
            width,
            direction,
            [VgaPortParameters.RawByte("dac index", 0x3C8, value)]);
    }

    private static DecodedCall DacData(uint value, int width, IoPortAccessDirection direction) {
        string name = direction == IoPortAccessDirection.Write ? "DAC Data Write (0x3C9)" : "DAC Data Read (0x3C9)";
        string description = direction == IoPortAccessDirection.Write
            ? "Writes one of three 6-bit color components (R/G/B) for the entry selected via 0x3C8; auto-increments after each triplet."
            : "Reads one of three 6-bit color components (R/G/B) for the entry selected via 0x3C7; auto-increments after each triplet.";
        return Build(name, description, 0x3C9, value, width, direction,
            [VgaPortParameters.RawByte("component (6-bit)", 0x3C9, value)]);
    }

    private static DecodedCall FeatureControlRead(uint value, int width, IoPortAccessDirection direction) {
        return Build(
            "Feature Control Read (0x3CA)",
            "Returns FC0/FC1 status bits.",
            0x3CA,
            value,
            width,
            direction,
            [VgaPortParameters.RawByte("value", 0x3CA, value)]);
    }

    private static DecodedCall MiscOutputRead(uint value, int width, IoPortAccessDirection direction) {
        return Build(
            "Misc Output Read (0x3CC)",
            "Returns the value last written to 0x3C2 (VGA only).",
            0x3CC,
            value,
            width,
            direction,
            [VgaPortParameters.RawByte("misc", 0x3CC, value)]);
    }

    private static DecodedCall GraphicsControllerAddress(uint value, int width, IoPortAccessDirection direction) {
        if (direction == IoPortAccessDirection.Write) {
            byte index = (byte)(value & 0xFF);
            string name = VgaIndexedRegisterTables.Lookup(VgaIndexedRegisterTables.GraphicsController, index);
            return Build(
                "Graphics Controller Address Write (0x3CE)",
                "Selects the Graphics Controller register addressed by subsequent reads/writes to 0x3CF.",
                0x3CE,
                value,
                width,
                direction,
                [VgaPortParameters.IndexWithName("GC index", 0x3CE, index, name)]);
        }
        return Build(
            "Graphics Controller Address Read (0x3CE)",
            "Returns the currently selected Graphics Controller index.",
            0x3CE,
            value,
            width,
            direction,
            [VgaPortParameters.RawByte("value", 0x3CE, value)]);
    }

    private static DecodedCall GraphicsControllerData(uint value, int width, IoPortAccessDirection direction) {
        string name = direction == IoPortAccessDirection.Write ? "Graphics Controller Data Write (0x3CF)" : "Graphics Controller Data Read (0x3CF)";
        string description = direction == IoPortAccessDirection.Write
            ? "Writes to the Graphics Controller register selected via 0x3CE."
            : "Reads the Graphics Controller register selected via 0x3CE.";
        return Build(name, description, 0x3CF, value, width, direction,
            [VgaPortParameters.RawByte("value", 0x3CF, value)]);
    }

    private static DecodedCall CgaModeControl(uint value, int width, IoPortAccessDirection direction) {
        return Build(
            "CGA Mode Control (0x3D8)",
            "CGA-compat register: bit 0 = 80x25 text, bit 1 = graphics, bit 2 = mono, bit 3 = video enable, bit 4 = 640x200 mono, bit 5 = blink.",
            0x3D8,
            value,
            width,
            direction,
            [VgaPortParameters.RawByte("mode", 0x3D8, value)]);
    }

    private static DecodedCall CgaColorSelect(uint value, int width, IoPortAccessDirection direction) {
        return Build(
            "CGA Color Select (0x3D9)",
            "CGA-compat register: bits 3-0 select border/background and intensity; bit 5 picks one of two 320x200 palettes.",
            0x3D9,
            value,
            width,
            direction,
            [VgaPortParameters.RawByte("value", 0x3D9, value)]);
    }

    private static DecodedCall Unknown(ushort port, uint value, int width, IoPortAccessDirection direction) {
        return Build(
            $"Unknown VGA Port 0x{port:X3}",
            "No specific decoder for this port — falling back to the generic value.",
            port,
            value,
            width,
            direction,
            [VgaPortParameters.RawByte("value", port, value)]);
    }

    private static DecodedCall Build(
        string functionName,
        string shortDescription,
        ushort port,
        uint value,
        int width,
        IoPortAccessDirection direction,
        IReadOnlyList<DecodedParameter> parameters) {
        string verb = direction == IoPortAccessDirection.Read ? "in" : "out";
        string desc = $"{verb} 0x{port:X3} (width={width}): {shortDescription}";
        _ = value;
        return new DecodedCall(Subsystem, functionName, desc, parameters, []);
    }
}
