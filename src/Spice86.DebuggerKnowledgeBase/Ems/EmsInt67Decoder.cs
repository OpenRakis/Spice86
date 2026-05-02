namespace Spice86.DebuggerKnowledgeBase.Ems;

using System.Collections.Generic;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.DebuggerKnowledgeBase.Bios;
using Spice86.DebuggerKnowledgeBase.Decoding;

/// <summary>
/// Decodes INT 67h (LIM EMS, Expanded Memory Manager) calls. Mirrors the dispatch table in
/// <c>ExpandedMemoryManager</c>, covering EMS 3.2 plus the EMS 4.0 sub-functions the emulator
/// implements (50h, 51h, 53h, 58h, 59h).
/// </summary>
public sealed class EmsInt67Decoder : IInterruptDecoder {
    private const string Subsystem = "EMS INT 67h";

    /// <summary>EMS page frame is 64 KB at E000:0000 by default in the emulator.</summary>
    private const ushort EmmNullPage = 0xFFFF;

    private static readonly IReadOnlyDictionary<byte, BiosFunctionEntry> ByAh = new Dictionary<byte, BiosFunctionEntry> {
        [0x40] = new BiosFunctionEntry("Get Manager Status", "Return manager presence/status in AH (00h = OK)."),
        [0x41] = new BiosFunctionEntry("Get Page Frame Segment", "Return the segment of the 64 KB page frame in BX."),
        [0x42] = new BiosFunctionEntry("Get Unallocated Page Count", "Return total pages in DX and free pages in BX."),
        [0x43] = new BiosFunctionEntry("Allocate Pages", "Allocate BX 16 KB pages and return the new handle in DX."),
        [0x44] = new BiosFunctionEntry("Map/Unmap Handle Page", "Map logical page BX of handle DX to physical page AL (BX=FFFFh = unmap)."),
        [0x45] = new BiosFunctionEntry("Deallocate Pages", "Free all pages owned by handle DX."),
        [0x46] = new BiosFunctionEntry("Get EMM Version", "Return BCD version in AL (this manager: 3.2)."),
        [0x47] = new BiosFunctionEntry("Save Page Map", "Save the page-map context for handle DX."),
        [0x48] = new BiosFunctionEntry("Restore Page Map", "Restore the page-map context previously saved for handle DX."),
        [0x4B] = new BiosFunctionEntry("Get Handle Count", "Return the number of open handles in BX."),
        [0x4C] = new BiosFunctionEntry("Get Handle Pages", "Return the number of pages assigned to handle DX in BX."),
        [0x4D] = new BiosFunctionEntry("Get All Handle Pages", "Write {handle, pages}* table to ES:DI; return entry count in BX."),
        [0x50] = new BiosFunctionEntry("Map/Unmap Multiple Handle Pages", "Map CX entries from DS:SI for handle DX (AL=0 physical, 1 segmented)."),
        [0x51] = new BiosFunctionEntry("Reallocate Pages", "Reallocate handle DX so it owns BX pages."),
        [0x53] = new BiosFunctionEntry("Get/Set Handle Name", "AL=0 get / 1 set the 8-byte name of handle DX."),
        [0x58] = new BiosFunctionEntry("Get Mappable Physical Address Array", "Write the {segment, page#} table to ES:DI; return entry count in CX."),
        [0x59] = new BiosFunctionEntry("Get Hardware Information", "AL=0 hardware-config array at ES:DI; AL=1 raw page counts in BX,DX.")
    };

    /// <inheritdoc />
    public bool CanDecode(byte vector) {
        return vector == 0x67;
    }

    /// <inheritdoc />
    public DecodedCall Decode(byte vector, State state, IMemory memory) {
        byte ah = state.AH;
        BiosFunctionEntry entry;
        if (ByAh.TryGetValue(ah, out BiosFunctionEntry? known)) {
            entry = known;
        } else {
            entry = new BiosFunctionEntry($"AH={ah:X2}h (unknown)", "Unknown EMS INT 67h sub-function.");
        }
        IReadOnlyList<DecodedParameter> parameters = DecodeParameters(ah, state);
        return new DecodedCall(Subsystem, $"AH={ah:X2}h {entry.Name}", entry.Description, parameters, []);
    }

    private static IReadOnlyList<DecodedParameter> DecodeParameters(byte ah, State state) {
        if (ah == 0x43) {
            return [BiosParameter.Decimal("pages", "BX", state.BX)];
        }
        if (ah == 0x44) {
            DecodedParameter logical = LogicalPage(state.BX);
            return [
                BiosParameter.Decimal("physical page", "AL", state.AL),
                logical,
                Handle(state.DX)
            ];
        }
        if (ah == 0x45 || ah == 0x47 || ah == 0x48 || ah == 0x4C) {
            return [Handle(state.DX)];
        }
        if (ah == 0x4D || ah == 0x58) {
            return [BiosParameter.SegmentedPointer("buffer", "ES:DI", state.ES, state.DI)];
        }
        if (ah == 0x50) {
            return [
                MultipleMapMode(state.AL),
                BiosParameter.Decimal("count", "CX", state.CX),
                Handle(state.DX),
                BiosParameter.SegmentedPointer("map", "DS:SI", state.DS, state.SI)
            ];
        }
        if (ah == 0x51) {
            return [
                BiosParameter.Decimal("new pages", "BX", state.BX),
                Handle(state.DX)
            ];
        }
        if (ah == 0x53) {
            return [
                HandleNameMode(state.AL),
                Handle(state.DX)
            ];
        }
        if (ah == 0x59) {
            return [HardwareInfoMode(state.AL)];
        }
        return [];
    }

    private static DecodedParameter Handle(ushort handle) {
        return new DecodedParameter(
            "handle",
            "DX",
            DecodedParameterKind.Register,
            handle,
            $"{handle} (0x{handle:X4})",
            null);
    }

    private static DecodedParameter LogicalPage(ushort logicalPage) {
        string formatted;
        string? note;
        if (logicalPage == EmmNullPage) {
            formatted = "0xFFFF";
            note = "unmap";
        } else {
            formatted = $"{logicalPage} (0x{logicalPage:X4})";
            note = null;
        }
        return new DecodedParameter(
            "logical page",
            "BX",
            DecodedParameterKind.Register,
            logicalPage,
            formatted,
            note);
    }

    private static DecodedParameter MultipleMapMode(byte mode) {
        string description;
        if (mode == 0x00) {
            description = "physical page numbers";
        } else if (mode == 0x01) {
            description = "segmented addresses";
        } else {
            description = "unknown";
        }
        return new DecodedParameter(
            "mode",
            "AL",
            DecodedParameterKind.Register,
            mode,
            $"0x{mode:X2} ({description})",
            null);
    }

    private static DecodedParameter HandleNameMode(byte mode) {
        string description;
        if (mode == 0x00) {
            description = "get";
        } else if (mode == 0x01) {
            description = "set";
        } else {
            description = "unknown";
        }
        return new DecodedParameter(
            "operation",
            "AL",
            DecodedParameterKind.Register,
            mode,
            $"0x{mode:X2} ({description})",
            null);
    }

    private static DecodedParameter HardwareInfoMode(byte mode) {
        string description;
        if (mode == 0x00) {
            description = "hardware configuration array";
        } else if (mode == 0x01) {
            description = "unallocated raw page counts";
        } else {
            description = "unknown";
        }
        return new DecodedParameter(
            "sub-function",
            "AL",
            DecodedParameterKind.Register,
            mode,
            $"0x{mode:X2} ({description})",
            null);
    }
}
