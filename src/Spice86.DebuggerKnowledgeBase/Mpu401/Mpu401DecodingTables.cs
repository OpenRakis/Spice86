namespace Spice86.DebuggerKnowledgeBase.Mpu401;

using System.Collections.Generic;

/// <summary>
/// Static lookup tables for the MPU-401 / General MIDI / MT-32 knowledge base.
/// </summary>
/// <remarks>
/// Names mirror the canonical Roland MPU-401 documentation as used by dosbox-staging's
/// <c>mpu401.cpp</c>. Both Roland General MIDI synths and the Roland MT-32 expose the
/// same MPU-401 register interface; only the MIDI payload semantics differ.
/// </remarks>
internal static class Mpu401DecodingTables {
    /// <summary>
    /// MPU-401 command codes written to the Command port (base + 1).
    /// </summary>
    public static readonly IReadOnlyDictionary<byte, string> Commands = new Dictionary<byte, string> {
        [0x3F] = "Enter UART mode",
        [0x80] = "Internal clock select",
        [0x81] = "MIDI clock select",
        [0x82] = "FSK clock select",
        [0x83] = "Metronome enable (without accents)",
        [0x84] = "Metronome disable",
        [0x85] = "Metronome enable (with accents)",
        [0x86] = "Bender on",
        [0x87] = "Bender off",
        [0x88] = "MIDI Through on",
        [0x89] = "MIDI Through off",
        [0x8A] = "Real Time Affection on",
        [0x8B] = "Real Time Affection off",
        [0x8C] = "Real Time Affection out only",
        [0x8E] = "Conductor off",
        [0x8F] = "Conductor on",
        [0x90] = "Real Time Out on",
        [0x91] = "Real Time Out off",
        [0x94] = "Clock to host disable",
        [0x95] = "Clock to host enable",
        [0x96] = "Exclusive thru disable",
        [0x97] = "Exclusive thru enable",
        [0xAB] = "Request and clear recording counter",
        [0xAC] = "Request version",
        [0xAD] = "Request revision",
        [0xAE] = "Request status",
        [0xAF] = "Request tempo",
        [0xB1] = "Reset relative tempo",
        [0xB8] = "Clear play counters",
        [0xB9] = "Clear play map",
        [0xBA] = "Clear record counter",
        [0xC2] = "Set timebase 48",
        [0xC3] = "Set timebase 72",
        [0xC4] = "Set timebase 96",
        [0xC5] = "Set timebase 120",
        [0xC6] = "Set timebase 144",
        [0xC7] = "Set timebase 168",
        [0xC8] = "Set timebase 192",
        [0xDF] = "Send system message",
        [0xE0] = "Set tempo (next byte)",
        [0xE1] = "Set relative tempo (next byte)",
        [0xE2] = "Set graduation for relative tempo (next byte)",
        [0xE4] = "Set metronome (next byte)",
        [0xE6] = "Set metronome measure length (next byte)",
        [0xE7] = "Set internal clock to host interval (next byte)",
        [0xEC] = "Set active track mask (next byte)",
        [0xED] = "Set play counter mask (next byte)",
        [0xEE] = "Set MIDI channel mask 1-8 (next byte)",
        [0xEF] = "Set MIDI channel mask 9-16 (next byte)",
        [0xFE] = "ACK (sent by MPU; never written by host)",
        [0xFF] = "Reset MPU-401"
    };

    /// <summary>
    /// MIDI status-byte names used when decoding bytes written to the Data port (base + 0).
    /// Indexed by the upper nibble of the status byte (0x80..0xE0) plus a few system-common
    /// values (0xF0..0xFF). The lower nibble of channel-voice messages encodes the channel.
    /// </summary>
    public static readonly IReadOnlyDictionary<byte, string> MidiStatusBytes = new Dictionary<byte, string> {
        [0x80] = "Note Off",
        [0x90] = "Note On",
        [0xA0] = "Polyphonic Aftertouch",
        [0xB0] = "Control Change",
        [0xC0] = "Program Change",
        [0xD0] = "Channel Aftertouch",
        [0xE0] = "Pitch Bend",
        [0xF0] = "System Exclusive (start)",
        [0xF1] = "MIDI Time Code Quarter Frame",
        [0xF2] = "Song Position Pointer",
        [0xF3] = "Song Select",
        [0xF6] = "Tune Request",
        [0xF7] = "End of Exclusive",
        [0xF8] = "Timing Clock",
        [0xFA] = "Start",
        [0xFB] = "Continue",
        [0xFC] = "Stop",
        [0xFE] = "Active Sensing",
        [0xFF] = "Reset"
    };

    /// <summary>Returns the entry for the index, or "Unknown" when not in the table.</summary>
    public static string Lookup(IReadOnlyDictionary<byte, string> table, byte index) {
        if (table.TryGetValue(index, out string? name)) {
            return name;
        }
        return "Unknown";
    }

    /// <summary>
    /// Returns a human-readable name for a MIDI byte. For channel-voice status bytes
    /// (0x80..0xEF) the lower nibble is decoded as the MIDI channel (1..16). Data bytes
    /// (high bit clear, 0x00..0x7F) are described as "data byte". Unknown system bytes
    /// fall back to "Unknown".
    /// </summary>
    public static string DescribeMidiByte(byte value) {
        if (value < 0x80) {
            return "data byte";
        }
        if (value < 0xF0) {
            byte status = (byte)(value & 0xF0);
            int channel = (value & 0x0F) + 1;
            string name = Lookup(MidiStatusBytes, status);
            return $"{name} (channel {channel})";
        }
        return Lookup(MidiStatusBytes, value);
    }
}
