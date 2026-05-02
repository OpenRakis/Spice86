namespace Spice86.DebuggerKnowledgeBase.Bios;

using System.Collections.Generic;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.DebuggerKnowledgeBase.Decoding;

/// <summary>
/// Decodes BIOS INT 10h (video services) calls. Mirrors the dispatch table in <c>VgaBios</c>.
/// </summary>
public sealed class BiosInt10Decoder : IInterruptDecoder {
    private const string Subsystem = "BIOS INT 10h";

    private static readonly IReadOnlyDictionary<byte, BiosFunctionEntry> ByAh = new Dictionary<byte, BiosFunctionEntry> {
        [0x00] = new BiosFunctionEntry("Set Video Mode", "Set the video mode in AL."),
        [0x01] = new BiosFunctionEntry("Set Cursor Type", "Set the cursor scan-line range from CH/CL."),
        [0x02] = new BiosFunctionEntry("Set Cursor Position", "Set cursor on page BH to row DH, column DL."),
        [0x03] = new BiosFunctionEntry("Get Cursor Position", "Return cursor position and shape for page BH."),
        [0x04] = new BiosFunctionEntry("Read Light Pen Position", "Return light pen state and position."),
        [0x05] = new BiosFunctionEntry("Select Active Display Page", "Set the active page to AL."),
        [0x06] = new BiosFunctionEntry("Scroll Page Up", "Scroll a window up by AL lines (0 = clear)."),
        [0x07] = new BiosFunctionEntry("Scroll Page Down", "Scroll a window down by AL lines (0 = clear)."),
        [0x08] = new BiosFunctionEntry("Read Char/Attribute at Cursor", "Read the character and attribute at the cursor."),
        [0x09] = new BiosFunctionEntry("Write Char/Attribute at Cursor", "Write character AL with attribute BL, CX times."),
        [0x0A] = new BiosFunctionEntry("Write Character at Cursor", "Write character AL at the cursor, CX times."),
        [0x0B] = new BiosFunctionEntry("Set Color Palette / Background", "Set background or palette depending on BH."),
        [0x0C] = new BiosFunctionEntry("Write Pixel", "Write color AL at (CX, DX) on page BH."),
        [0x0D] = new BiosFunctionEntry("Read Pixel", "Return color of pixel at (CX, DX) in AL."),
        [0x0E] = new BiosFunctionEntry("Teletype Output", "Write character AL to STDOUT, advancing the cursor."),
        [0x0F] = new BiosFunctionEntry("Get Video State", "Return current mode, columns, and active page."),
        [0x10] = new BiosFunctionEntry("Set Palette Registers", "Palette/DAC sub-functions selected by AL."),
        [0x11] = new BiosFunctionEntry("Load Font Info", "Font sub-functions selected by AL."),
        [0x12] = new BiosFunctionEntry("Video Subsystem Configuration", "Configuration sub-functions selected by BL."),
        [0x13] = new BiosFunctionEntry("Write String", "Write a string at ES:BP, length CX, attribute BL."),
        [0x1A] = new BiosFunctionEntry("Get/Set Display Combination Code", "Read or set the display combination code."),
        [0x1B] = new BiosFunctionEntry("Get Functionality/State Info", "Write functionality info into ES:DI."),
        [0x4F] = new BiosFunctionEntry("VESA Functions", "VESA SVGA sub-functions selected by AL.")
    };

    /// <inheritdoc />
    public bool CanDecode(byte vector) {
        return vector == 0x10;
    }

    /// <inheritdoc />
    public DecodedCall Decode(byte vector, State state, IMemory memory) {
        byte ah = state.AH;
        BiosFunctionEntry entry = Lookup(ah);
        IReadOnlyList<DecodedParameter> parameters = DecodeParameters(ah, state);
        return new DecodedCall(
            Subsystem,
            $"AH={ah:X2}h {entry.Name}",
            entry.Description,
            parameters,
            []);
    }

    private static BiosFunctionEntry Lookup(byte ah) {
        if (ByAh.TryGetValue(ah, out BiosFunctionEntry? entry)) {
            return entry;
        }
        return new BiosFunctionEntry($"AH={ah:X2}h (unknown)", "Unknown BIOS INT 10h sub-function.");
    }

    private static IReadOnlyList<DecodedParameter> DecodeParameters(byte ah, State state) {
        if (ah == 0x00) {
            return [BiosParameter.VideoMode("mode", "AL", state.AL)];
        }
        if (ah == 0x01) {
            return [
                BiosParameter.Byte("start scanline", "CH", state.CH),
                BiosParameter.Byte("end scanline", "CL", state.CL)
            ];
        }
        if (ah == 0x02) {
            return [
                BiosParameter.Byte("page", "BH", state.BH),
                BiosParameter.Decimal("row", "DH", state.DH),
                BiosParameter.Decimal("column", "DL", state.DL)
            ];
        }
        if (ah == 0x03 || ah == 0x05) {
            string source;
            byte value;
            if (ah == 0x03) {
                source = "BH";
                value = state.BH;
            } else {
                source = "AL";
                value = state.AL;
            }
            return [BiosParameter.Byte("page", source, value)];
        }
        if (ah == 0x06 || ah == 0x07) {
            return [
                BiosParameter.Byte("lines", "AL", state.AL, "0 = clear window"),
                BiosParameter.Byte("attribute", "BH", state.BH),
                BiosParameter.Decimal("top row", "CH", state.CH),
                BiosParameter.Decimal("left col", "CL", state.CL),
                BiosParameter.Decimal("bottom row", "DH", state.DH),
                BiosParameter.Decimal("right col", "DL", state.DL)
            ];
        }
        if (ah == 0x08) {
            return [BiosParameter.Byte("page", "BH", state.BH)];
        }
        if (ah == 0x09 || ah == 0x0A) {
            return [
                BiosParameter.Character("character", "AL", state.AL),
                BiosParameter.Byte("page", "BH", state.BH),
                BiosParameter.Byte("attribute", "BL", state.BL),
                BiosParameter.Word("count", "CX", state.CX)
            ];
        }
        if (ah == 0x0C) {
            return [
                BiosParameter.Byte("color", "AL", state.AL),
                BiosParameter.Byte("page", "BH", state.BH),
                BiosParameter.Decimal("x", "CX", state.CX),
                BiosParameter.Decimal("y", "DX", state.DX)
            ];
        }
        if (ah == 0x0D) {
            return [
                BiosParameter.Byte("page", "BH", state.BH),
                BiosParameter.Decimal("x", "CX", state.CX),
                BiosParameter.Decimal("y", "DX", state.DX)
            ];
        }
        if (ah == 0x0E) {
            return [
                BiosParameter.Character("character", "AL", state.AL),
                BiosParameter.Byte("page", "BH", state.BH),
                BiosParameter.Byte("color", "BL", state.BL)
            ];
        }
        if (ah == 0x10 || ah == 0x11 || ah == 0x4F) {
            return [BiosParameter.Hex("sub-function", "AL", state.AL)];
        }
        if (ah == 0x12) {
            return [BiosParameter.Hex("sub-function", "BL", state.BL)];
        }
        if (ah == 0x13) {
            return [
                BiosParameter.Hex("write mode", "AL", state.AL),
                BiosParameter.Byte("page", "BH", state.BH),
                BiosParameter.Byte("attribute", "BL", state.BL),
                BiosParameter.Word("length", "CX", state.CX),
                BiosParameter.Decimal("row", "DH", state.DH),
                BiosParameter.Decimal("column", "DL", state.DL),
                BiosParameter.SegmentedPointer("string", "ES:BP", state.ES, state.BP)
            ];
        }
        return [];
    }
}
