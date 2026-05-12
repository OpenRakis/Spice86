namespace Spice86.DebuggerKnowledgeBase.Bios;

using System.Collections.Generic;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.DebuggerKnowledgeBase.Decoding;

/// <summary>
/// Decodes BIOS INT 10h (video services) calls. Mirrors the dispatch table in <c>VgaBios</c> and
/// the AH/AL/BL/BH sub-function tables documented by Ralf Brown's interrupt list and implemented
/// by dosbox-staging's <c>int10.cpp</c> handler (covering CGA, EGA, VGA, VESA SVGA, EGA Register
/// Interface Library, and the Video Save/Restore area).
/// </summary>
public sealed class BiosInt10Decoder : IInterruptDecoder {
    private const string Subsystem = "BIOS INT 10h";

    private static readonly IReadOnlyDictionary<byte, BiosFunctionEntry> ByAh = new Dictionary<byte, BiosFunctionEntry> {
        [0x00] = new BiosFunctionEntry("Set Video Mode", "Set the video mode in AL."),
        [0x01] = new BiosFunctionEntry("Set Cursor Type", "Set the cursor scan-line range from CH/CL."),
        [0x02] = new BiosFunctionEntry("Set Cursor Position", "Set cursor on page BH to row DH, column DL."),
        [0x03] = new BiosFunctionEntry("Get Cursor Position", "Return cursor position and shape for page BH."),
        [0x04] = new BiosFunctionEntry("Read Light Pen Position", "Return light pen state and position."),
        [0x05] = new BiosFunctionEntry("Select Active Display Page", "Set the active page to AL (PCjr/Tandy: AL>=80h selects CRT/CPU page)."),
        [0x06] = new BiosFunctionEntry("Scroll Page Up", "Scroll a window up by AL lines (0 = clear)."),
        [0x07] = new BiosFunctionEntry("Scroll Page Down", "Scroll a window down by AL lines (0 = clear)."),
        [0x08] = new BiosFunctionEntry("Read Char/Attribute at Cursor", "Read the character and attribute at the cursor."),
        [0x09] = new BiosFunctionEntry("Write Char/Attribute at Cursor", "Write character AL with attribute BL, CX times."),
        [0x0A] = new BiosFunctionEntry("Write Character at Cursor", "Write character AL at the cursor, CX times."),
        [0x0B] = new BiosFunctionEntry("Set Color Palette / Background", "BH=0 set background/border color BL; BH=1 set CGA palette BL."),
        [0x0C] = new BiosFunctionEntry("Write Pixel", "Write color AL at (CX, DX) on page BH."),
        [0x0D] = new BiosFunctionEntry("Read Pixel", "Return color of pixel at (CX, DX) in AL."),
        [0x0E] = new BiosFunctionEntry("Teletype Output", "Write character AL to STDOUT, advancing the cursor."),
        [0x0F] = new BiosFunctionEntry("Get Video State", "Return current mode (AL), columns (AH), and active page (BH)."),
        [0x10] = new BiosFunctionEntry("Palette / DAC Registers", "EGA/VGA palette and DAC sub-functions selected by AL."),
        [0x11] = new BiosFunctionEntry("Character Generator", "Font load/activate and font-info sub-functions selected by AL."),
        [0x12] = new BiosFunctionEntry("Alternate Function Select", "EGA/VGA configuration sub-functions selected by BL."),
        [0x13] = new BiosFunctionEntry("Write String", "Write a string at ES:BP, length CX, attribute BL."),
        [0x1A] = new BiosFunctionEntry("Display Combination Code", "AL=0 get DCC, AL=1 set DCC; BX = primary/alternate DCC."),
        [0x1B] = new BiosFunctionEntry("Get Functionality/State Info", "Write functionality info (64 bytes) into ES:DI when BX=0."),
        [0x1C] = new BiosFunctionEntry("Video Save/Restore Area", "AL sub-function: 0=size, 1=save, 2=restore. CX=state mask, ES:BX=buffer."),
        [0x4F] = new BiosFunctionEntry("VESA SVGA Functions", "VESA SVGA sub-functions selected by AL."),
        [0xF0] = new BiosFunctionEntry("EGA RIL: Read One Register", "EGA Register Interface Library read single register (BL=index, DX=port group)."),
        [0xF1] = new BiosFunctionEntry("EGA RIL: Write One Register", "EGA Register Interface Library write single register (BL=index, BH=value, DX=port group)."),
        [0xF2] = new BiosFunctionEntry("EGA RIL: Read Register Range", "Read a contiguous range of registers into ES:BX. CL=count, CH=start, DX=port group."),
        [0xF3] = new BiosFunctionEntry("EGA RIL: Write Register Range", "Write a contiguous range of registers from ES:BX. CL=count, CH=start, DX=port group."),
        [0xF4] = new BiosFunctionEntry("EGA RIL: Read Register Set", "Read an arbitrary set of registers described at ES:BX (CX entries)."),
        [0xF5] = new BiosFunctionEntry("EGA RIL: Write Register Set", "Write an arbitrary set of registers described at ES:BX (CX entries)."),
        [0xFA] = new BiosFunctionEntry("EGA RIL: Get Version Pointer", "Return ES:BX pointer to the EGA Register Interface Library version structure."),
        [0xFF] = new BiosFunctionEntry("Update Whole Screen", "Undocumented NC-DOS / VGA helper; tells the BIOS to refresh the entire screen.")
    };

    private static readonly IReadOnlyDictionary<byte, string> Sub10ByAl = new Dictionary<byte, string> {
        [0x00] = "Set Single Palette Register",
        [0x01] = "Set Border (Overscan) Color",
        [0x02] = "Set All Palette Registers",
        [0x03] = "Toggle Intensity/Blinking Bit",
        [0x07] = "Get Single Palette Register",
        [0x08] = "Read Overscan (Border Color) Register",
        [0x09] = "Read All Palette Registers and Overscan",
        [0x10] = "Set Individual DAC Register",
        [0x12] = "Set Block of DAC Registers",
        [0x13] = "Select Video DAC Color Page",
        [0x15] = "Get Individual DAC Register",
        [0x17] = "Get Block of DAC Registers",
        [0x18] = "Set PEL Mask",
        [0x19] = "Get PEL Mask",
        [0x1A] = "Get Video DAC Color Page",
        [0x1B] = "Perform Gray-Scale Summing",
        [0xF0] = "ET4000: Set HiColor Graphics Mode",
        [0xF1] = "ET4000: Get DAC Type",
        [0xF2] = "ET4000: Check/Set HiColor Mode"
    };

    private static readonly IReadOnlyDictionary<byte, string> Sub11ByAl = new Dictionary<byte, string> {
        [0x00] = "Load User Font",
        [0x01] = "Load ROM 8x14 Font",
        [0x02] = "Load ROM 8x8 Font",
        [0x03] = "Set Block Specifier",
        [0x04] = "Load ROM 8x16 Font",
        [0x10] = "Load and Activate User Font",
        [0x11] = "Load and Activate ROM 8x14 Font",
        [0x12] = "Load and Activate ROM 8x8 Font",
        [0x14] = "Load and Activate ROM 8x16 Font",
        [0x20] = "Set User 8x8 Graphics Characters (INT 1Fh)",
        [0x21] = "Set User Graphics Characters (INT 43h)",
        [0x22] = "Set ROM 8x14 Graphics Font",
        [0x23] = "Set ROM 8x8 Graphics Font",
        [0x24] = "Set ROM 8x16 Graphics Font",
        [0x30] = "Get Font Information"
    };

    // AH=11h AL=30h: BH selects which font/vector to return.
    private static readonly IReadOnlyDictionary<byte, string> Sub1130ByBh = new Dictionary<byte, string> {
        [0x00] = "INT 1Fh vector (8x8 user graphics)",
        [0x01] = "INT 43h vector (user graphics)",
        [0x02] = "ROM 8x14 font",
        [0x03] = "ROM 8x8 font (first 128)",
        [0x04] = "ROM 8x8 font (second 128)",
        [0x05] = "ROM 9x14 alpha alternate",
        [0x06] = "ROM 8x16 font",
        [0x07] = "ROM 9x16 alpha alternate"
    };

    private static readonly IReadOnlyDictionary<byte, string> Sub12ByBl = new Dictionary<byte, string> {
        [0x10] = "Get EGA Information",
        [0x20] = "Set Alternate Print-Screen",
        [0x30] = "Select Vertical Resolution (200/350/400)",
        [0x31] = "Default Palette Loading on Mode Set",
        [0x32] = "Video Addressing Enable/Disable",
        [0x33] = "Gray-Scale Summing Enable/Disable",
        [0x34] = "Cursor Emulation Enable/Disable",
        [0x35] = "Display Switch (active/inactive)",
        [0x36] = "Video Refresh Control (screen on/off)"
    };

    private static readonly IReadOnlyDictionary<byte, string> Sub4FByAl = new Dictionary<byte, string> {
        [0x00] = "Get SVGA Information (ES:DI buffer)",
        [0x01] = "Get SVGA Mode Information (CX=mode, ES:DI buffer)",
        [0x02] = "Set SVGA Video Mode (BX=mode)",
        [0x03] = "Get Current SVGA Video Mode",
        [0x04] = "Save/Restore SVGA State",
        [0x05] = "Display Window Control",
        [0x06] = "Set/Get Logical Scan Line Length",
        [0x07] = "Set/Get Display Start",
        [0x08] = "Set/Get DAC Palette Format",
        [0x09] = "Set/Get Palette Data",
        [0x0A] = "Get Protected-Mode Interface"
    };

    private static readonly IReadOnlyDictionary<byte, string> Sub1CByAl = new Dictionary<byte, string> {
        [0x00] = "Get Save/Restore State Buffer Size",
        [0x01] = "Save Video State",
        [0x02] = "Restore Video State"
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
        if (ah == 0x03) {
            return [BiosParameter.Byte("page", "BH", state.BH)];
        }
        if (ah == 0x05) {
            return [BiosParameter.Byte("page", "AL", state.AL)];
        }
        if (ah == 0x06 || ah == 0x07) {
            return [
                BiosParameter.Byte("lines", "AL", state.AL, "0 = clear window"),
                BiosParameter.Byte("blank attribute", "BH", state.BH),
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
        if (ah == 0x0B) {
            string? bhMnemonic;
            if (state.BH == 0x00) {
                bhMnemonic = "set background/border color";
            } else if (state.BH == 0x01) {
                bhMnemonic = "set CGA palette";
            } else {
                bhMnemonic = null;
            }
            return [
                BiosParameter.NamedSubFunction("function", "BH", state.BH, bhMnemonic),
                BiosParameter.Byte("color/palette", "BL", state.BL)
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
        if (ah == 0x10) {
            return DecodeSub10(state);
        }
        if (ah == 0x11) {
            return DecodeSub11(state);
        }
        if (ah == 0x12) {
            string? blMnemonic = LookupName(Sub12ByBl, state.BL);
            return [BiosParameter.NamedSubFunction("sub-function", "BL", state.BL, blMnemonic)];
        }
        if (ah == 0x13) {
            return [
                BiosParameter.Hex("write mode", "AL", state.AL, "bit 0=update cursor, bit 1=string contains attributes"),
                BiosParameter.Byte("page", "BH", state.BH),
                BiosParameter.Byte("attribute", "BL", state.BL),
                BiosParameter.Word("length", "CX", state.CX),
                BiosParameter.Decimal("row", "DH", state.DH),
                BiosParameter.Decimal("column", "DL", state.DL),
                BiosParameter.SegmentedPointer("string", "ES:BP", state.ES, state.BP)
            ];
        }
        if (ah == 0x1A) {
            string? alMnemonic;
            if (state.AL == 0x00) {
                alMnemonic = "Get DCC";
            } else if (state.AL == 0x01) {
                alMnemonic = "Set DCC";
            } else {
                alMnemonic = null;
            }
            return [
                BiosParameter.NamedSubFunction("function", "AL", state.AL, alMnemonic),
                BiosParameter.Hex("DCC", "BX", state.BX)
            ];
        }
        if (ah == 0x1B) {
            return [
                BiosParameter.Hex("info index", "BX", state.BX),
                BiosParameter.SegmentedPointer("buffer", "ES:DI", state.ES, state.DI)
            ];
        }
        if (ah == 0x1C) {
            string? alMnemonic = LookupName(Sub1CByAl, state.AL);
            DecodedParameter sub = BiosParameter.NamedSubFunction("function", "AL", state.AL, alMnemonic);
            DecodedParameter mask = BiosParameter.Hex("state mask", "CX", state.CX);
            if (state.AL == 0x01 || state.AL == 0x02) {
                return [
                    sub,
                    mask,
                    BiosParameter.SegmentedPointer("buffer", "ES:BX", state.ES, state.BX)
                ];
            }
            return [sub, mask];
        }
        if (ah == 0x4F) {
            return DecodeSub4F(state);
        }
        if (ah == 0xF0 || ah == 0xF1) {
            // EGA RIL read/write single register; for write, BH carries the value.
            if (ah == 0xF1) {
                return [
                    BiosParameter.Byte("register index", "BL", state.BL),
                    BiosParameter.Byte("value", "BH", state.BH),
                    BiosParameter.Hex("port group", "DX", state.DX)
                ];
            }
            return [
                BiosParameter.Byte("register index", "BL", state.BL),
                BiosParameter.Hex("port group", "DX", state.DX)
            ];
        }
        if (ah == 0xF2 || ah == 0xF3) {
            return [
                BiosParameter.Byte("count", "CL", state.CL),
                BiosParameter.Byte("start index", "CH", state.CH),
                BiosParameter.Hex("port group", "DX", state.DX),
                BiosParameter.SegmentedPointer("buffer", "ES:BX", state.ES, state.BX)
            ];
        }
        if (ah == 0xF4 || ah == 0xF5) {
            return [
                BiosParameter.Word("entry count", "CX", state.CX),
                BiosParameter.SegmentedPointer("register set", "ES:BX", state.ES, state.BX)
            ];
        }
        return [];
    }

    private static IReadOnlyList<DecodedParameter> DecodeSub10(State state) {
        string? alMnemonic = LookupName(Sub10ByAl, state.AL);
        DecodedParameter sub = BiosParameter.NamedSubFunction("sub-function", "AL", state.AL, alMnemonic);
        if (state.AL == 0x00) {
            return [
                sub,
                BiosParameter.Byte("palette index", "BL", state.BL),
                BiosParameter.Byte("color value", "BH", state.BH)
            ];
        }
        if (state.AL == 0x01 || state.AL == 0x08) {
            return [sub, BiosParameter.Byte("border color", "BH", state.BH)];
        }
        if (state.AL == 0x02 || state.AL == 0x09) {
            return [sub, BiosParameter.SegmentedPointer("palette table", "ES:DX", state.ES, state.DX)];
        }
        if (state.AL == 0x03) {
            return [sub, BiosParameter.Byte("blink/intensity", "BL", state.BL, "0=intensity, 1=blink")];
        }
        if (state.AL == 0x07) {
            return [sub, BiosParameter.Byte("palette index", "BL", state.BL)];
        }
        if (state.AL == 0x10) {
            return [
                sub,
                BiosParameter.Word("DAC index", "BX", state.BX),
                BiosParameter.Byte("red", "DH", state.DH),
                BiosParameter.Byte("green", "CH", state.CH),
                BiosParameter.Byte("blue", "CL", state.CL)
            ];
        }
        if (state.AL == 0x12 || state.AL == 0x17) {
            return [
                sub,
                BiosParameter.Word("first DAC index", "BX", state.BX),
                BiosParameter.Word("count", "CX", state.CX),
                BiosParameter.SegmentedPointer("RGB table", "ES:DX", state.ES, state.DX)
            ];
        }
        if (state.AL == 0x13) {
            return [
                sub,
                BiosParameter.Byte("function", "BL", state.BL, "0=select page mode, 1=select page"),
                BiosParameter.Byte("page", "BH", state.BH)
            ];
        }
        if (state.AL == 0x15) {
            return [sub, BiosParameter.Word("DAC index", "BX", state.BX)];
        }
        if (state.AL == 0x18 || state.AL == 0x19) {
            return [sub, BiosParameter.Byte("PEL mask", "BL", state.BL)];
        }
        if (state.AL == 0x1B) {
            return [
                sub,
                BiosParameter.Word("first DAC index", "BX", state.BX),
                BiosParameter.Word("count", "CX", state.CX)
            ];
        }
        return [sub];
    }

    private static IReadOnlyList<DecodedParameter> DecodeSub11(State state) {
        string? alMnemonic = LookupName(Sub11ByAl, state.AL);
        DecodedParameter sub = BiosParameter.NamedSubFunction("sub-function", "AL", state.AL, alMnemonic);
        // User-loaded fonts (00/10) carry full parameters; ROM-load variants only need block + height.
        if (state.AL == 0x00 || state.AL == 0x10) {
            return [
                sub,
                BiosParameter.Byte("char height", "BH", state.BH),
                BiosParameter.Byte("font block", "BL", state.BL),
                BiosParameter.Word("char count", "CX", state.CX),
                BiosParameter.Word("first char", "DX", state.DX),
                BiosParameter.SegmentedPointer("font data", "ES:BP", state.ES, state.BP)
            ];
        }
        if (state.AL == 0x01 || state.AL == 0x02 || state.AL == 0x04
            || state.AL == 0x11 || state.AL == 0x12 || state.AL == 0x14) {
            return [sub, BiosParameter.Byte("font block", "BL", state.BL)];
        }
        if (state.AL == 0x03) {
            return [sub, BiosParameter.Byte("block specifier", "BL", state.BL)];
        }
        if (state.AL >= 0x20 && state.AL <= 0x24) {
            // Set graphics chars: BL = rows-mode, DL = rows count (BL=0 only); CX/ES:BP for AL=21h.
            if (state.AL == 0x20 || state.AL == 0x21) {
                return [
                    sub,
                    BiosParameter.Byte("rows mode", "BL", state.BL, "0=DL rows, 1=14, 2=25, 3=43"),
                    BiosParameter.Decimal("rows (BL=0)", "DL", state.DL),
                    BiosParameter.Word("char height", "CX", state.CX),
                    BiosParameter.SegmentedPointer("font data", "ES:BP", state.ES, state.BP)
                ];
            }
            return [
                sub,
                BiosParameter.Byte("rows mode", "BL", state.BL, "0=DL rows, 1=14, 2=25, 3=43"),
                BiosParameter.Decimal("rows (BL=0)", "DL", state.DL)
            ];
        }
        if (state.AL == 0x30) {
            string? bhMnemonic = LookupName(Sub1130ByBh, state.BH);
            return [
                sub,
                BiosParameter.NamedSubFunction("font/vector", "BH", state.BH, bhMnemonic)
            ];
        }
        return [sub];
    }

    private static IReadOnlyList<DecodedParameter> DecodeSub4F(State state) {
        string? alMnemonic = LookupName(Sub4FByAl, state.AL);
        DecodedParameter sub = BiosParameter.NamedSubFunction("sub-function", "AL", state.AL, alMnemonic);
        if (state.AL == 0x00) {
            return [sub, BiosParameter.SegmentedPointer("info buffer", "ES:DI", state.ES, state.DI)];
        }
        if (state.AL == 0x01) {
            return [
                sub,
                BiosParameter.Hex("mode", "CX", state.CX),
                BiosParameter.SegmentedPointer("mode info", "ES:DI", state.ES, state.DI)
            ];
        }
        if (state.AL == 0x02) {
            return [sub, BiosParameter.Hex("mode", "BX", state.BX)];
        }
        if (state.AL == 0x04) {
            return [
                sub,
                BiosParameter.Byte("operation", "DL", state.DL, "0=size, 1=save, 2=restore"),
                BiosParameter.Hex("state mask", "CX", state.CX),
                BiosParameter.SegmentedPointer("buffer", "ES:BX", state.ES, state.BX)
            ];
        }
        if (state.AL == 0x05) {
            return [
                sub,
                BiosParameter.Byte("operation", "BH", state.BH, "0=set window, 1=get window"),
                BiosParameter.Byte("window id", "BL", state.BL),
                BiosParameter.Word("window number", "DX", state.DX)
            ];
        }
        if (state.AL == 0x06) {
            return [
                sub,
                BiosParameter.Byte("operation", "BL", state.BL, "0=set, 1=get, 2=set in pixels, 3=get max length"),
                BiosParameter.Word("pixels/bytes", "CX", state.CX)
            ];
        }
        if (state.AL == 0x07) {
            return [
                sub,
                BiosParameter.Byte("operation", "BL", state.BL, "0=set, 1=get, 80h=set during retrace"),
                BiosParameter.Word("first pixel", "CX", state.CX),
                BiosParameter.Word("first scan line", "DX", state.DX)
            ];
        }
        if (state.AL == 0x09) {
            return [
                sub,
                BiosParameter.Byte("operation", "BL", state.BL, "0=set, 1=get, 80h=set during retrace"),
                BiosParameter.Word("count", "CX", state.CX),
                BiosParameter.Word("first index", "DX", state.DX),
                BiosParameter.SegmentedPointer("palette table", "ES:DI", state.ES, state.DI)
            ];
        }
        if (state.AL == 0x0A) {
            return [sub, BiosParameter.Byte("which", "BL", state.BL, "0=full, 1=set window, 2=set start, 3=set palette")];
        }
        return [sub];
    }

    private static string? LookupName(IReadOnlyDictionary<byte, string> table, byte key) {
        if (table.TryGetValue(key, out string? name)) {
            return name;
        }
        return null;
    }
}
