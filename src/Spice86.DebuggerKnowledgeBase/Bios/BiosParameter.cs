namespace Spice86.DebuggerKnowledgeBase.Bios;

using System.Globalization;

using Spice86.DebuggerKnowledgeBase.Decoding;

/// <summary>
/// Small builders for common BIOS parameter shapes (single byte or word register, segmented
/// pointer). Pure helpers, internal to the BIOS knowledge base.
/// </summary>
internal static class BiosParameter {
    public static DecodedParameter Byte(string name, string source, byte value, string? note = null) {
        return new DecodedParameter(
            name,
            source,
            DecodedParameterKind.Register,
            value,
            $"{value} (0x{value:X2})",
            note);
    }

    public static DecodedParameter Word(string name, string source, ushort value, string? note = null) {
        return new DecodedParameter(
            name,
            source,
            DecodedParameterKind.Register,
            value,
            $"{value} (0x{value:X4})",
            note);
    }

    public static DecodedParameter Hex(string name, string source, byte value, string? note = null) {
        return new DecodedParameter(
            name,
            source,
            DecodedParameterKind.Register,
            value,
            $"0x{value:X2}",
            note);
    }

    public static DecodedParameter Hex(string name, string source, ushort value, string? note = null) {
        return new DecodedParameter(
            name,
            source,
            DecodedParameterKind.Register,
            value,
            $"0x{value:X4}",
            note);
    }

    public static DecodedParameter SegmentedPointer(string name, string source, ushort segment, ushort offset, string? note = null) {
        long combined = ((long)segment << 16) | offset;
        return new DecodedParameter(
            name,
            source,
            DecodedParameterKind.Register,
            combined,
            $"{segment:X4}:{offset:X4}",
            note);
    }

    public static DecodedParameter Character(string name, string source, byte value, string? note = null) {
        string formatted;
        if (value >= 0x20 && value < 0x7F) {
            formatted = $"'{(char)value}' (0x{value:X2})";
        } else {
            formatted = $"0x{value:X2}";
        }
        return new DecodedParameter(name, source, DecodedParameterKind.Register, value, formatted, note);
    }

    public static DecodedParameter Drive(string name, string source, byte value) {
        string formatted;
        if ((value & 0x80) != 0) {
            int hd = value & 0x7F;
            formatted = $"hard disk {hd} (0x{value:X2})";
        } else {
            formatted = $"floppy {value} (0x{value:X2})";
        }
        return new DecodedParameter(name, source, DecodedParameterKind.Register, value, formatted, null);
    }

    public static DecodedParameter VideoMode(string name, string source, byte value) {
        // Mnemonics follow the BIOS mode numbering as documented by Ralf Brown's interrupt list and
        // mirrored by dosbox-staging. Mode 7 is MDA monochrome text; modes 8/9/0Ah are PCjr/Tandy
        // 16-color graphics; 0Bh/0Ch are EGA-reserved (BIOS uses them internally during mode set).
        string description;
        if (value == 0x00) {
            description = "40x25 text, 16-gray (CGA)";
        } else if (value == 0x01) {
            description = "40x25 text, 16-color (CGA)";
        } else if (value == 0x02) {
            description = "80x25 text, 16-gray (CGA)";
        } else if (value == 0x03) {
            description = "80x25 text, 16-color (CGA)";
        } else if (value == 0x04) {
            description = "320x200 4-color CGA";
        } else if (value == 0x05) {
            description = "320x200 4-color CGA (alt palette)";
        } else if (value == 0x06) {
            description = "640x200 mono CGA";
        } else if (value == 0x07) {
            description = "80x25 text, MDA mono";
        } else if (value == 0x08) {
            description = "160x200 16-color (PCjr/Tandy)";
        } else if (value == 0x09) {
            description = "320x200 16-color (PCjr/Tandy)";
        } else if (value == 0x0A) {
            description = "640x200 4-color (PCjr/Tandy)";
        } else if (value == 0x0B) {
            description = "EGA reserved (internal)";
        } else if (value == 0x0C) {
            description = "EGA reserved (internal)";
        } else if (value == 0x0D) {
            description = "320x200 16-color EGA";
        } else if (value == 0x0E) {
            description = "640x200 16-color EGA";
        } else if (value == 0x0F) {
            description = "640x350 mono EGA";
        } else if (value == 0x10) {
            description = "640x350 16-color EGA";
        } else if (value == 0x11) {
            description = "640x480 mono VGA";
        } else if (value == 0x12) {
            description = "640x480 16-color VGA";
        } else if (value == 0x13) {
            description = "320x200 256-color VGA";
        } else {
            description = $"mode 0x{value:X2}";
        }
        return new DecodedParameter(
            name,
            source,
            DecodedParameterKind.Register,
            value,
            $"0x{value:X2} ({description})",
            null);
    }

    public static DecodedParameter Decimal(string name, string source, ushort value) {
        return new DecodedParameter(
            name,
            source,
            DecodedParameterKind.Register,
            value,
            value.ToString(CultureInfo.InvariantCulture),
            null);
    }

    /// <summary>
    /// Builds a register parameter for a sub-function selector with a known mnemonic, formatted as
    /// "0x{value:X2} ({mnemonic})". When <paramref name="mnemonic"/> is null the value is rendered
    /// as plain hex (matches <see cref="Hex(string,string,byte,string)"/>).
    /// </summary>
    public static DecodedParameter NamedSubFunction(string name, string source, byte value, string? mnemonic) {
        string formatted;
        if (mnemonic is null) {
            formatted = $"0x{value:X2}";
        } else {
            formatted = $"0x{value:X2} ({mnemonic})";
        }
        return new DecodedParameter(name, source, DecodedParameterKind.Register, value, formatted, null);
    }
}
