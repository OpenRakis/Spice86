namespace Spice86.DebuggerKnowledgeBase.Xms;

using System.Collections.Generic;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.DebuggerKnowledgeBase.Bios;
using Spice86.DebuggerKnowledgeBase.Decoding;
using Spice86.Shared.Utils;

/// <summary>
/// Decodes a call to the XMS driver entry point. XMS is invoked via a far call to the address
/// returned by INT 2Fh AX=4310h, not via a software interrupt, so this decoder is not part of
/// the interrupt registry; the UI / debugger calls it directly when the program is at the XMS
/// callback entry point.
/// </summary>
/// <remarks>
/// Mirrors the dispatch in <c>ExtendedMemoryManager.RunMultiplex</c>: AH selects the
/// sub-function. Decoders are pure - they read CPU state and memory but never mutate them.
/// </remarks>
public sealed class XmsCallDecoder {
    private const string Subsystem = "XMS Driver";

    /// <summary>
    /// Decodes the XMS call currently set up in <paramref name="state"/>.
    /// </summary>
    /// <param name="state">Current CPU state.</param>
    /// <param name="memory">Emulated memory bus, used to dereference the move structure on AH=0Bh.</param>
    public DecodedCall Decode(State state, IMemory memory) {
        byte ah = state.AH;
        XmsFunctionEntry entry;
        if (XmsDecodingTables.ByAh.TryGetValue(ah, out XmsFunctionEntry? known)) {
            entry = known;
        } else {
            entry = new XmsFunctionEntry($"AH={ah:X2}h (unknown)", "Unknown XMS sub-function.");
        }
        IReadOnlyList<DecodedParameter> parameters = DecodeParameters(ah, state, memory);
        return new DecodedCall(Subsystem, $"AH={ah:X2}h {entry.Name}", entry.Description, parameters, []);
    }

    private static IReadOnlyList<DecodedParameter> DecodeParameters(byte ah, State state, IMemory memory) {
        if (ah == 0x01) {
            return [HmaRequestSize(state.DX)];
        }
        if (ah == 0x09) {
            return [BiosParameter.Decimal("kbytes", "DX", state.DX)];
        }
        if (ah == 0x89) {
            return [Kbytes32("kbytes", "EDX", state.EDX)];
        }
        if (ah == 0x0A || ah == 0x0C || ah == 0x0D || ah == 0x0E || ah == 0x8E) {
            return [Handle(state.DX)];
        }
        if (ah == 0x0B) {
            return DecodeMove(state, memory);
        }
        if (ah == 0x0F) {
            return [
                BiosParameter.Decimal("new kbytes", "BX", state.BX),
                Handle(state.DX)
            ];
        }
        if (ah == 0x8F) {
            return [
                Kbytes32("new kbytes", "EBX", state.EBX),
                Handle(state.DX)
            ];
        }
        if (ah == 0x10 || ah == 0x12) {
            return [
                BiosParameter.Decimal("paragraphs", "DX", state.DX)
            ];
        }
        if (ah == 0x11) {
            return [
                new DecodedParameter("UMB segment", "DX", DecodedParameterKind.Register, state.DX, $"0x{state.DX:X4}", null)
            ];
        }
        return [];
    }

    private static IReadOnlyList<DecodedParameter> DecodeMove(State state, IMemory memory) {
        ushort segment = state.DS;
        ushort offset = state.SI;
        uint baseAddress = MemoryUtils.ToPhysicalAddress(segment, offset);
        uint length = memory.UInt32[baseAddress + 0x0u];
        ushort sourceHandle = memory.UInt16[baseAddress + 0x4u];
        uint sourceOffset = memory.UInt32[baseAddress + 0x6u];
        ushort destHandle = memory.UInt16[baseAddress + 0xAu];
        uint destOffset = memory.UInt32[baseAddress + 0xCu];
        return [
            BiosParameter.SegmentedPointer("move struct", "DS:SI", segment, offset),
            new DecodedParameter("length", "[DS:SI+0]", DecodedParameterKind.Memory, length, $"{length} (0x{length:X8}) bytes", null),
            HandleOrZero("source handle", "[DS:SI+4]", sourceHandle),
            SourceOrDest("source offset", "[DS:SI+6]", sourceHandle, sourceOffset),
            HandleOrZero("dest handle", "[DS:SI+A]", destHandle),
            SourceOrDest("dest offset", "[DS:SI+C]", destHandle, destOffset)
        ];
    }

    private static DecodedParameter HmaRequestSize(ushort size) {
        string formatted;
        string? note;
        if (size == 0xFFFF) {
            formatted = "0xFFFF";
            note = "TSR / device driver (entire HMA)";
        } else {
            formatted = $"{size} (0x{size:X4})";
            note = "bytes";
        }
        return new DecodedParameter("requested size", "DX", DecodedParameterKind.Register, size, formatted, note);
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

    private static DecodedParameter Kbytes32(string name, string source, uint value) {
        return new DecodedParameter(
            name,
            source,
            DecodedParameterKind.Register,
            value,
            $"{value} (0x{value:X8})",
            null);
    }

    private static DecodedParameter HandleOrZero(string name, string source, ushort handle) {
        string formatted;
        string? note;
        if (handle == 0x0000) {
            formatted = "0x0000";
            note = "real-mode segment:offset (offset field is seg:off)";
        } else {
            formatted = $"{handle} (0x{handle:X4})";
            note = null;
        }
        return new DecodedParameter(name, source, DecodedParameterKind.Memory, handle, formatted, note);
    }

    private static DecodedParameter SourceOrDest(string name, string source, ushort handle, uint value) {
        string formatted;
        string? note;
        if (handle == 0x0000) {
            ushort segment = (ushort)(value >> 16);
            ushort offset = (ushort)value;
            formatted = $"{segment:X4}:{offset:X4}";
            note = "real-mode pointer";
        } else {
            formatted = $"0x{value:X8}";
            note = "32-bit offset into XMS handle";
        }
        return new DecodedParameter(name, source, DecodedParameterKind.Memory, value, formatted, note);
    }
}
