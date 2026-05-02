namespace Spice86.DebuggerKnowledgeBase.Bios;

using System.Collections.Generic;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.DebuggerKnowledgeBase.Decoding;

/// <summary>
/// Decodes BIOS INT 16h (keyboard services) calls. Mirrors <c>KeyboardInt16Handler</c>.
/// </summary>
public sealed class BiosInt16Decoder : IInterruptDecoder {
    private const string Subsystem = "BIOS INT 16h";

    private static readonly IReadOnlyDictionary<byte, BiosFunctionEntry> ByAh = new Dictionary<byte, BiosFunctionEntry> {
        [0x00] = new BiosFunctionEntry("Get Keystroke", "Block until a key is pressed; return ASCII in AL, scan code in AH."),
        [0x01] = new BiosFunctionEntry("Get Keystroke Status", "Set ZF if no key in buffer; otherwise return next key in AX without consuming it."),
        [0x02] = new BiosFunctionEntry("Get Shift Flags", "Return the keyboard shift state in AL."),
        [0x03] = new BiosFunctionEntry("Set Typematic Rate and Delay", "Configure repeat rate (BL) and delay (BH)."),
        [0x05] = new BiosFunctionEntry("Push Keystroke", "Insert keystroke CX into the BIOS buffer."),
        [0x10] = new BiosFunctionEntry("Get Enhanced Keystroke", "Block until a key is pressed (extended key support)."),
        [0x11] = new BiosFunctionEntry("Get Enhanced Keystroke Status", "Like AH=01h but with extended key support."),
        [0x1D] = new BiosFunctionEntry("Unsupported (1Dh)", "Vendor-specific; not implemented.")
    };

    /// <inheritdoc />
    public bool CanDecode(byte vector) {
        return vector == 0x16;
    }

    /// <inheritdoc />
    public DecodedCall Decode(byte vector, State state, IMemory memory) {
        byte ah = state.AH;
        BiosFunctionEntry entry;
        if (ByAh.TryGetValue(ah, out BiosFunctionEntry? known)) {
            entry = known;
        } else {
            entry = new BiosFunctionEntry($"AH={ah:X2}h (unknown)", "Unknown BIOS INT 16h sub-function.");
        }
        IReadOnlyList<DecodedParameter> parameters = DecodeParameters(ah, state);
        return new DecodedCall(Subsystem, $"AH={ah:X2}h {entry.Name}", entry.Description, parameters, []);
    }

    private static IReadOnlyList<DecodedParameter> DecodeParameters(byte ah, State state) {
        if (ah == 0x03) {
            return [
                BiosParameter.Hex("delay", "BH", state.BH, "0=250ms, 1=500ms, 2=750ms, 3=1s"),
                BiosParameter.Hex("rate", "BL", state.BL)
            ];
        }
        if (ah == 0x05) {
            ushort cx = state.CX;
            byte scan = (byte)(cx >> 8);
            byte ascii = (byte)(cx & 0xFF);
            return [
                BiosParameter.Hex("scan code", "CH", scan),
                BiosParameter.Character("character", "CL", ascii)
            ];
        }
        return [];
    }
}
