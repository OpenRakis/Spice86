namespace Spice86.DebuggerKnowledgeBase.Bios;

using System.Collections.Generic;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.DebuggerKnowledgeBase.Decoding;

/// <summary>
/// Decodes BIOS INT 15h (system services) calls. Mirrors <c>SystemBiosInt15Handler</c>.
/// AH=C2h is the PS/2 Pointing Device BIOS Interface (BIOS-level mouse). The DOS driver-level
/// mouse interface lives at INT 33h and is decoded by <c>BiosInt33Decoder</c>.
/// </summary>
public sealed class BiosInt15Decoder : IInterruptDecoder {
    private const string Subsystem = "BIOS INT 15h";

    private static readonly IReadOnlyDictionary<byte, string> PointingDeviceSubFunctions = new Dictionary<byte, string> {
        [0x00] = "Enable/Disable Pointing Device (BH=00 disable, 01 enable)",
        [0x01] = "Reset Pointing Device",
        [0x02] = "Set Sample Rate (BH = code: 0=10/s, 1=20/s, 2=40/s, 3=60/s, 4=80/s, 5=100/s, 6=200/s)",
        [0x03] = "Set Resolution (BH = 0=25, 1=50, 2=100, 3=200 counts/mm)",
        [0x04] = "Read Device Type",
        [0x05] = "Initialize Pointing Device (BH = data package size, 1..8)",
        [0x06] = "Extended Commands (BH = 0 status, 1 scaling 1:1, 2 scaling 2:1)",
        [0x07] = "Set Device Handler Address (ES:BX = far pointer to handler)"
    };

    private static readonly IReadOnlyDictionary<byte, BiosFunctionEntry> ByAh = new Dictionary<byte, BiosFunctionEntry> {
        [0x06] = new BiosFunctionEntry("Unsupported (06h)", "Unsupported on this BIOS."),
        [0x24] = new BiosFunctionEntry("A20 Gate Control", "AL=00 disable, 01 enable, 02 status, 03 supported."),
        [0x4F] = new BiosFunctionEntry("Keyboard Intercept", "Hook called by INT 9h for each scan code."),
        [0x50] = new BiosFunctionEntry("DOS/V Font Subsystem", "DOS/V font services selected by AL."),
        [0x83] = new BiosFunctionEntry("Event Wait", "Schedule a wait or cancel via AL."),
        [0x86] = new BiosFunctionEntry("Wait", "Wait CX:DX microseconds."),
        [0x87] = new BiosFunctionEntry("Copy Extended Memory", "Block-copy CX words via the descriptor at ES:SI."),
        [0x88] = new BiosFunctionEntry("Get Extended Memory Size", "Return extended memory size (KB) in AX."),
        [0x90] = new BiosFunctionEntry("Device Busy", "Notify BIOS that a device is busy."),
        [0x91] = new BiosFunctionEntry("Device Post", "Notify BIOS that a device has completed."),
        [0xC0] = new BiosFunctionEntry("Get System Configuration", "Return ES:BX pointing at the configuration table."),
        [0xC2] = new BiosFunctionEntry("Pointing Device Interface", "PS/2 mouse sub-functions via AL."),
        [0xC4] = new BiosFunctionEntry("PS/2 POS Programming", "Programmable Option Select sub-functions via AL.")
    };

    /// <inheritdoc />
    public bool CanDecode(byte vector) {
        return vector == 0x15;
    }

    /// <inheritdoc />
    public DecodedCall Decode(byte vector, State state, IMemory memory) {
        byte ah = state.AH;
        BiosFunctionEntry entry;
        if (ByAh.TryGetValue(ah, out BiosFunctionEntry? known)) {
            entry = known;
        } else {
            entry = new BiosFunctionEntry($"AH={ah:X2}h (unknown)", "Unknown BIOS INT 15h sub-function.");
        }
        IReadOnlyList<DecodedParameter> parameters = DecodeParameters(ah, state);
        return new DecodedCall(Subsystem, $"AH={ah:X2}h {entry.Name}", entry.Description, parameters, []);
    }

    private static IReadOnlyList<DecodedParameter> DecodeParameters(byte ah, State state) {
        if (ah == 0xC2) {
            return DecodePointingDeviceParameters(state);
        }
        if (ah == 0x24 || ah == 0xC4 || ah == 0x50 || ah == 0x83) {
            return [BiosParameter.Hex("sub-function", "AL", state.AL)];
        }
        if (ah == 0x86) {
            ushort cx = state.CX;
            ushort dx = state.DX;
            long combined = ((long)cx << 16) | dx;
            return [new DecodedParameter("microseconds", "CX:DX", DecodedParameterKind.Register, combined, $"{combined} us", null)];
        }
        if (ah == 0x87) {
            return [
                BiosParameter.Decimal("words", "CX", state.CX),
                BiosParameter.SegmentedPointer("descriptor", "ES:SI", state.ES, state.SI)
            ];
        }
        if (ah == 0x4F) {
            return [BiosParameter.Hex("scancode", "AL", state.AL)];
        }
        return [];
    }

    private static IReadOnlyList<DecodedParameter> DecodePointingDeviceParameters(State state) {
        byte al = state.AL;
        string description;
        if (PointingDeviceSubFunctions.TryGetValue(al, out string? known)) {
            description = known;
        } else {
            description = "unknown sub-function";
        }
        DecodedParameter subFunction = new DecodedParameter(
            "sub-function",
            "AL",
            DecodedParameterKind.Register,
            al,
            $"0x{al:X2} ({description})",
            null);
        if (al == 0x00) {
            byte bh = state.BH;
            string action;
            if (bh == 0x00) {
                action = "disable";
            } else if (bh == 0x01) {
                action = "enable";
            } else {
                action = "unknown";
            }
            return [
                subFunction,
                new DecodedParameter("action", "BH", DecodedParameterKind.Register, bh, $"0x{bh:X2} ({action})", null)
            ];
        }
        if (al == 0x02 || al == 0x03 || al == 0x05 || al == 0x06) {
            return [subFunction, BiosParameter.Hex("argument", "BH", state.BH)];
        }
        if (al == 0x07) {
            return [subFunction, BiosParameter.SegmentedPointer("handler", "ES:BX", state.ES, state.BX)];
        }
        return [subFunction];
    }
}
