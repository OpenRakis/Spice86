namespace Spice86.DebuggerKnowledgeBase.Bios;

using System.Collections.Generic;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.DebuggerKnowledgeBase.Decoding;

/// <summary>
/// Decodes BIOS INT 1Ah (system clock / RTC) calls. Mirrors <c>SystemClockInt1AHandler</c>.
/// </summary>
public sealed class BiosInt1ADecoder : IInterruptDecoder {
    private const string Subsystem = "BIOS INT 1Ah";

    private static readonly IReadOnlyDictionary<byte, BiosFunctionEntry> ByAh = new Dictionary<byte, BiosFunctionEntry> {
        [0x00] = new BiosFunctionEntry("Get System Tick Counter", "Return CX:DX = number of timer ticks since midnight; AL = midnight rollover flag."),
        [0x01] = new BiosFunctionEntry("Set System Tick Counter", "Set the timer tick counter to CX:DX."),
        [0x02] = new BiosFunctionEntry("Read RTC Time", "Return BCD time: hours in CH, minutes in CL, seconds in DH; DL = DST."),
        [0x03] = new BiosFunctionEntry("Set RTC Time", "Set RTC from BCD: CH hours, CL minutes, DH seconds, DL DST."),
        [0x04] = new BiosFunctionEntry("Read RTC Date", "Return BCD date: century in CH, year in CL, month in DH, day in DL."),
        [0x05] = new BiosFunctionEntry("Set RTC Date", "Set RTC from BCD: century CH, year CL, month DH, day DL.")
    };

    /// <inheritdoc />
    public bool CanDecode(byte vector) {
        return vector == 0x1A;
    }

    /// <inheritdoc />
    public DecodedCall Decode(byte vector, State state, IMemory memory) {
        byte ah = state.AH;
        BiosFunctionEntry entry;
        if (ByAh.TryGetValue(ah, out BiosFunctionEntry? known)) {
            entry = known;
        } else {
            entry = new BiosFunctionEntry($"AH={ah:X2}h (unknown)", "Unknown BIOS INT 1Ah sub-function.");
        }
        IReadOnlyList<DecodedParameter> parameters = DecodeParameters(ah, state);
        return new DecodedCall(Subsystem, $"AH={ah:X2}h {entry.Name}", entry.Description, parameters, []);
    }

    private static IReadOnlyList<DecodedParameter> DecodeParameters(byte ah, State state) {
        if (ah == 0x01) {
            ushort cx = state.CX;
            ushort dx = state.DX;
            long combined = ((long)cx << 16) | dx;
            return [new DecodedParameter("ticks", "CX:DX", DecodedParameterKind.Register, combined, $"{combined}", null)];
        }
        if (ah == 0x03) {
            return [
                BiosParameter.Hex("hours (BCD)", "CH", state.CH),
                BiosParameter.Hex("minutes (BCD)", "CL", state.CL),
                BiosParameter.Hex("seconds (BCD)", "DH", state.DH),
                BiosParameter.Hex("DST flag", "DL", state.DL)
            ];
        }
        if (ah == 0x05) {
            return [
                BiosParameter.Hex("century (BCD)", "CH", state.CH),
                BiosParameter.Hex("year (BCD)", "CL", state.CL),
                BiosParameter.Hex("month (BCD)", "DH", state.DH),
                BiosParameter.Hex("day (BCD)", "DL", state.DL)
            ];
        }
        return [];
    }
}
