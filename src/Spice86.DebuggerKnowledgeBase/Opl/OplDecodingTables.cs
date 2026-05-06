namespace Spice86.DebuggerKnowledgeBase.Opl;

using System.Collections.Generic;

/// <summary>
/// Static lookup tables for the OPL (Yamaha YM3812 / YMF262) and AdLib Gold knowledge base.
/// Names mirror the canonical Yamaha datasheets and dosbox-staging's <c>opl.cpp</c> /
/// <c>adlib_gold.cpp</c>, plus the Spice86 <c>Opl3Fm</c> / <c>StereoProcessor</c> sources.
/// </summary>
internal static class OplDecodingTables {
    /// <summary>
    /// Returns the symbolic name for an OPL register address (0x000..0x1FF).
    /// </summary>
    /// <remarks>
    /// On OPL2 only addresses 0x000..0x0F5 are valid. OPL3 adds the array-1 mirror at
    /// 0x100..0x1F5 (selected via the secondary address port — base+2 / 0x38A). For
    /// banked addresses (per-operator / per-channel groups) the returned name includes
    /// the unit index decoded from the low nibble.
    /// </remarks>
    public static string LookupRegister(ushort address) {
        ushort bank = (ushort)(address & 0x100);
        ushort low = (ushort)(address & 0xFF);
        string bankPrefix;
        if (bank != 0) {
            bankPrefix = "Array-1 ";
        } else {
            bankPrefix = string.Empty;
        }

        switch (low) {
            case 0x01:
                if (bank != 0) {
                    return "Array-1 Test (reserved)";
                }
                return "Test / Waveform Select Enable (OPL2)";
            case 0x02:
                return bankPrefix + "Timer 1 Counter";
            case 0x03:
                return bankPrefix + "Timer 2 Counter";
            case 0x04:
                if (bank != 0) {
                    return "Array-1 4-Operator Connection Select (OPL3)";
                }
                return "Timer Control / IRQ Reset";
            case 0x05:
                if (bank != 0) {
                    return "Array-1 New / OPL3 Enable";
                }
                return "Reserved";
            case 0x08:
                return bankPrefix + "Composite Sine Wave / Note Select / NTS";
            case 0xBD:
                return bankPrefix + "Tremolo / Vibrato / Percussion Mode (Rhythm)";
        }

        if (low >= 0x20 && low <= 0x35) {
            return bankPrefix + $"AM/Vibrato/Sustain/KSR/Multiplier (operator {OperatorIndex(low - 0x20)})";
        }
        if (low >= 0x40 && low <= 0x55) {
            return bankPrefix + $"Key Scale Level / Total Level (operator {OperatorIndex(low - 0x40)})";
        }
        if (low >= 0x60 && low <= 0x75) {
            return bankPrefix + $"Attack Rate / Decay Rate (operator {OperatorIndex(low - 0x60)})";
        }
        if (low >= 0x80 && low <= 0x95) {
            return bankPrefix + $"Sustain Level / Release Rate (operator {OperatorIndex(low - 0x80)})";
        }
        if (low >= 0xE0 && low <= 0xF5) {
            return bankPrefix + $"Waveform Select (operator {OperatorIndex(low - 0xE0)})";
        }

        if (low >= 0xA0 && low <= 0xA8) {
            return bankPrefix + $"Frequency Number Low (channel {low - 0xA0})";
        }
        if (low >= 0xB0 && low <= 0xB8) {
            return bankPrefix + $"Key On / Block / Frequency Number High (channel {low - 0xB0})";
        }
        if (low >= 0xC0 && low <= 0xC8) {
            return bankPrefix + $"Feedback / Connection / Output Channel (channel {low - 0xC0})";
        }

        return bankPrefix + $"Unknown / Reserved (0x{low:X2})";
    }

    /// <summary>
    /// AdLib Gold control register names, selected via 0x38A (control index) and written
    /// via 0x38B (control data) once the control unit has been activated by writing 0xFF
    /// to 0x38A. Mirrors the switch in <c>Opl3Fm.PortWrite</c> at port 0x38B.
    /// </summary>
    public static readonly IReadOnlyDictionary<byte, string> AdlibGoldControls = new Dictionary<byte, string> {
        [0x04] = "Stereo Volume Left",
        [0x05] = "Stereo Volume Right",
        [0x06] = "Bass",
        [0x07] = "Treble",
        [0x08] = "Switch Functions (source select / stereo mode)",
        [0x09] = "Left FM Volume",
        [0x0A] = "Right FM Volume",
        [0x18] = "Surround Control (YM7128B serial)",
    };

    /// <summary>
    /// Convenience lookup that returns "Unknown" when the byte is not in the table.
    /// </summary>
    public static string Lookup(IReadOnlyDictionary<byte, string> table, byte key) {
        if (table.TryGetValue(key, out string? name)) {
            return name;
        }
        return "Unknown";
    }

    private static int OperatorIndex(int offsetInBank) {
        // Bank layout (offsets 0x00..0x15):
        //  row 0: ops 0..5  at +0x00..+0x05
        //  row 1: ops 6..11 at +0x08..+0x0D
        //  row 2: ops 12..17 at +0x10..+0x15
        if (offsetInBank <= 0x05) {
            return offsetInBank;
        }
        if (offsetInBank >= 0x08 && offsetInBank <= 0x0D) {
            return 6 + (offsetInBank - 0x08);
        }
        if (offsetInBank >= 0x10 && offsetInBank <= 0x15) {
            return 12 + (offsetInBank - 0x10);
        }
        return -1;
    }
}
