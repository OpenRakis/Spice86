namespace Spice86.DebuggerKnowledgeBase.Bios;

using System.Collections.Generic;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.DebuggerKnowledgeBase.Decoding;

/// <summary>
/// Decodes INT 33h (Microsoft mouse driver) calls. Mirrors <c>MouseInt33Handler</c>.
/// Although INT 33h is a software interrupt installed by a TSR (mouse driver) rather than ROM
/// BIOS, it lives in the BIOS knowledge module because it sits next to the keyboard / video
/// services in the debugger UI.
/// </summary>
public sealed class BiosInt33Decoder : IInterruptDecoder {
    private const string Subsystem = "Mouse INT 33h";

    private static readonly IReadOnlyDictionary<byte, BiosFunctionEntry> ByAx = new Dictionary<byte, BiosFunctionEntry> {
        [0x00] = new BiosFunctionEntry("Mouse Installation Check", "AX=0xFFFF and BX=button count if installed."),
        [0x01] = new BiosFunctionEntry("Show Mouse Cursor", "Increment cursor visibility counter."),
        [0x02] = new BiosFunctionEntry("Hide Mouse Cursor", "Decrement cursor visibility counter."),
        [0x03] = new BiosFunctionEntry("Get Position and Status", "Return CX=x, DX=y, BX=button state."),
        [0x04] = new BiosFunctionEntry("Set Cursor Position", "Move cursor to CX,DX."),
        [0x05] = new BiosFunctionEntry("Get Button Press Counter", "Return press count for button BX."),
        [0x06] = new BiosFunctionEntry("Get Button Release Counter", "Return release count for button BX."),
        [0x07] = new BiosFunctionEntry("Set Horizontal Range", "Clamp X to [CX, DX]."),
        [0x08] = new BiosFunctionEntry("Set Vertical Range", "Clamp Y to [CX, DX]."),
        [0x0B] = new BiosFunctionEntry("Get Mouse Motion", "Return CX,DX = mickeys since last call."),
        [0x0C] = new BiosFunctionEntry("Set User Callback", "Install handler ES:DX with event mask CX."),
        [0x0F] = new BiosFunctionEntry("Set Mickey/Pixel Ratio", "Configure horizontal CX and vertical DX mickeys per 8 pixels."),
        [0x13] = new BiosFunctionEntry("Set Double-Speed Threshold", "Threshold in DX mickeys/second."),
        [0x14] = new BiosFunctionEntry("Swap User Callback", "Replace callback with ES:DX, mask CX; return old in ES:DX/CX."),
        [0x1A] = new BiosFunctionEntry("Set Mouse Sensitivity", "Set thresholds (BX horizontal, CX vertical, DX double-speed)."),
        [0x1B] = new BiosFunctionEntry("Get Mouse Sensitivity", "Return current thresholds."),
        [0x1C] = new BiosFunctionEntry("Set Interrupt Rate", "BX selects the interrupt rate."),
        [0x21] = new BiosFunctionEntry("Reset Mouse", "Reset driver state."),
        [0x24] = new BiosFunctionEntry("Get Mouse Driver Version", "BX major.minor, CH = type, CL = IRQ.")
    };

    /// <inheritdoc />
    public bool CanDecode(byte vector) {
        return vector == 0x33;
    }

    /// <inheritdoc />
    public DecodedCall Decode(byte vector, State state, IMemory memory) {
        ushort ax = state.AX;
        byte selector = (byte)ax;
        BiosFunctionEntry entry;
        if (ax <= 0xFF && ByAx.TryGetValue(selector, out BiosFunctionEntry? known)) {
            entry = known;
        } else {
            entry = new BiosFunctionEntry($"AX={ax:X4}h (unknown)", "Unknown INT 33h sub-function.");
        }
        IReadOnlyList<DecodedParameter> parameters = DecodeParameters(selector, state);
        return new DecodedCall(Subsystem, $"AX={ax:X4}h {entry.Name}", entry.Description, parameters, []);
    }

    private static IReadOnlyList<DecodedParameter> DecodeParameters(byte selector, State state) {
        if (selector == 0x04) {
            return [
                BiosParameter.Decimal("x", "CX", state.CX),
                BiosParameter.Decimal("y", "DX", state.DX)
            ];
        }
        if (selector == 0x05 || selector == 0x06) {
            return [BiosParameter.Decimal("button", "BX", state.BX)];
        }
        if (selector == 0x07 || selector == 0x08) {
            return [
                BiosParameter.Decimal("min", "CX", state.CX),
                BiosParameter.Decimal("max", "DX", state.DX)
            ];
        }
        if (selector == 0x0C || selector == 0x14) {
            return [
                BiosParameter.Hex("event mask", "CX", state.CX),
                BiosParameter.SegmentedPointer("callback", "ES:DX", state.ES, state.DX)
            ];
        }
        if (selector == 0x0F) {
            return [
                BiosParameter.Decimal("h mickeys/8px", "CX", state.CX),
                BiosParameter.Decimal("v mickeys/8px", "DX", state.DX)
            ];
        }
        if (selector == 0x13) {
            return [BiosParameter.Decimal("mickeys/second", "DX", state.DX)];
        }
        if (selector == 0x1A) {
            return [
                BiosParameter.Decimal("h threshold", "BX", state.BX),
                BiosParameter.Decimal("v threshold", "CX", state.CX),
                BiosParameter.Decimal("double-speed", "DX", state.DX)
            ];
        }
        if (selector == 0x1C) {
            return [BiosParameter.Hex("rate code", "BX", state.BX)];
        }
        return [];
    }
}
