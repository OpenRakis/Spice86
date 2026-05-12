namespace Spice86.DebuggerKnowledgeBase.Dos;

using System.Collections.Generic;
using System.Globalization;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.DebuggerKnowledgeBase.Decoding;

/// <summary>
/// Decodes DOS INT 21h calls into <see cref="DecodedCall"/> values for the debugger UI. Pure:
/// reads CPU state and memory, never mutates them.
/// </summary>
public sealed class DosInt21Decoder : IInterruptDecoder {
    /// <summary>
    /// INT 21h is the only vector this decoder claims.
    /// </summary>
    public bool CanDecode(byte vector) {
        return vector == 0x21;
    }

    /// <inheritdoc />
    public DecodedCall Decode(byte vector, State state, IMemory memory) {
        byte ah = state.AH;
        DosInt21DecodingTables.FunctionEntry entry = DosInt21DecodingTables.GetEntry(ah);
        string functionName = $"AH={ah:X2}h {entry.Name}";
        IReadOnlyList<DecodedParameter> parameters = DecodeParameters(ah, state, memory);
        return new DecodedCall(
            DosInt21DecodingTables.Subsystem,
            functionName,
            entry.Description,
            parameters,
            []);
    }

    private static IReadOnlyList<DecodedParameter> DecodeParameters(byte ah, State state, IMemory memory) {
        if (ah == 0x02) {
            return [DlAsCharacter(state)];
        }
        if (ah == 0x05) {
            return [DlAsCharacter(state)];
        }
        if (ah == 0x09) {
            return [DsDxDollarString(state, memory)];
        }
        if (ah == 0x0E) {
            return [DriveByDl(state)];
        }
        if (ah == 0x1C) {
            return [DriveByDl(state)];
        }
        if (ah == 0x25) {
            return [VectorNumber(state), HandlerAddress(state)];
        }
        if (ah == 0x35) {
            return [VectorNumber(state)];
        }
        if (ah == 0x36) {
            return [DriveByDl(state)];
        }
        if (ah == 0x39 || ah == 0x3A || ah == 0x3B || ah == 0x41 || ah == 0x43) {
            return [DsDxAsciiZ("path", state, memory)];
        }
        if (ah == 0x3C) {
            return [DsDxAsciiZ("filename", state, memory), CxAttributes(state)];
        }
        if (ah == 0x3D) {
            return [DsDxAsciiZ("filename", state, memory), AlOpenAccessMode(state)];
        }
        if (ah == 0x3E) {
            return [BxFileHandle(state)];
        }
        if (ah == 0x3F || ah == 0x40) {
            return [BxFileHandle(state), CxByteCount(state), DsDxBufferAddress(state)];
        }
        if (ah == 0x42) {
            return [BxFileHandle(state), AlSeekMode(state), CxDxOffset(state)];
        }
        if (ah == 0x4C) {
            return [AlAsExitCode(state)];
        }
        if (ah == 0x4E) {
            return [DsDxAsciiZ("pattern", state, memory), CxAttributes(state)];
        }
        return [];
    }

    private static DecodedParameter DlAsCharacter(State state) {
        byte dl = state.DL;
        return new DecodedParameter(
            "character",
            "DL",
            DecodedParameterKind.Register,
            dl,
            FormatCharacter(dl),
            null);
    }

    private static DecodedParameter DriveByDl(State state) {
        byte dl = state.DL;
        char letter = (char)('A' + dl);
        return new DecodedParameter(
            "drive",
            "DL",
            DecodedParameterKind.Register,
            dl,
            $"{letter}: ({dl:X2}h)",
            null);
    }

    private static DecodedParameter VectorNumber(State state) {
        byte al = state.AL;
        return new DecodedParameter(
            "vector",
            "AL",
            DecodedParameterKind.Register,
            al,
            $"INT {al:X2}h",
            null);
    }

    private static DecodedParameter HandlerAddress(State state) {
        ushort segment = state.DS;
        ushort offset = state.DX;
        long combined = ((long)segment << 16) | offset;
        return new DecodedParameter(
            "handler",
            "DS:DX",
            DecodedParameterKind.Register,
            combined,
            $"{segment:X4}:{offset:X4}",
            null);
    }

    private static DecodedParameter DsDxAsciiZ(string name, State state, IMemory memory) {
        ushort segment = state.DS;
        ushort offset = state.DX;
        string str = DosMemoryReader.ReadAsciiZ(memory, segment, offset);
        long combined = ((long)segment << 16) | offset;
        return new DecodedParameter(
            name,
            "DS:DX",
            DecodedParameterKind.Memory,
            combined,
            $"\"{str}\" (at {segment:X4}:{offset:X4})",
            null);
    }

    private static DecodedParameter DsDxDollarString(State state, IMemory memory) {
        ushort segment = state.DS;
        ushort offset = state.DX;
        string str = DosMemoryReader.ReadDollarTerminated(memory, segment, offset);
        long combined = ((long)segment << 16) | offset;
        return new DecodedParameter(
            "string",
            "DS:DX",
            DecodedParameterKind.Memory,
            combined,
            $"\"{str}\" (at {segment:X4}:{offset:X4})",
            "Terminated by '$'.");
    }

    private static DecodedParameter DsDxBufferAddress(State state) {
        ushort segment = state.DS;
        ushort offset = state.DX;
        long combined = ((long)segment << 16) | offset;
        return new DecodedParameter(
            "buffer",
            "DS:DX",
            DecodedParameterKind.Register,
            combined,
            $"{segment:X4}:{offset:X4}",
            null);
    }

    private static DecodedParameter BxFileHandle(State state) {
        ushort bx = state.BX;
        return new DecodedParameter(
            "handle",
            "BX",
            DecodedParameterKind.Register,
            bx,
            FormatHandleValue(bx),
            null);
    }

    private static DecodedParameter CxByteCount(State state) {
        ushort cx = state.CX;
        return new DecodedParameter(
            "byte count",
            "CX",
            DecodedParameterKind.Register,
            cx,
            $"{cx} (0x{cx:X4})",
            null);
    }

    private static DecodedParameter CxAttributes(State state) {
        ushort cx = state.CX;
        return new DecodedParameter(
            "attributes",
            "CX",
            DecodedParameterKind.Register,
            cx,
            FormatFileAttributes(cx),
            null);
    }

    private static DecodedParameter AlOpenAccessMode(State state) {
        byte al = state.AL;
        string description;
        byte access = (byte)(al & 0x07);
        if (access == 0) {
            description = "read-only";
        } else if (access == 1) {
            description = "write-only";
        } else if (access == 2) {
            description = "read/write";
        } else {
            description = "unknown access";
        }
        return new DecodedParameter(
            "access mode",
            "AL",
            DecodedParameterKind.Register,
            al,
            $"0x{al:X2} = {description}",
            null);
    }

    private static DecodedParameter AlSeekMode(State state) {
        byte al = state.AL;
        string mode;
        if (al == 0) {
            mode = "from start";
        } else if (al == 1) {
            mode = "from current";
        } else if (al == 2) {
            mode = "from end";
        } else {
            mode = "unknown";
        }
        return new DecodedParameter(
            "seek mode",
            "AL",
            DecodedParameterKind.Register,
            al,
            $"0x{al:X2} = {mode}",
            null);
    }

    private static DecodedParameter CxDxOffset(State state) {
        ushort cx = state.CX;
        ushort dx = state.DX;
        long combined = ((long)cx << 16) | dx;
        return new DecodedParameter(
            "offset",
            "CX:DX",
            DecodedParameterKind.Register,
            combined,
            combined.ToString(CultureInfo.InvariantCulture) + $" (0x{combined:X8})",
            null);
    }

    private static DecodedParameter AlAsExitCode(State state) {
        byte al = state.AL;
        return new DecodedParameter(
            "exit code",
            "AL",
            DecodedParameterKind.Register,
            al,
            $"{al} (0x{al:X2})",
            null);
    }

    private static string FormatCharacter(byte b) {
        if (b >= 0x20 && b < 0x7F) {
            return $"'{(char)b}' (0x{b:X2})";
        }
        return $"0x{b:X2}";
    }

    private static string FormatHandleValue(ushort bx) {
        if (bx == 0) {
            return "0 (STDIN)";
        }
        if (bx == 1) {
            return "1 (STDOUT)";
        }
        if (bx == 2) {
            return "2 (STDERR)";
        }
        if (bx == 3) {
            return "3 (STDAUX)";
        }
        if (bx == 4) {
            return "4 (STDPRN)";
        }
        return bx.ToString(CultureInfo.InvariantCulture);
    }

    private static string FormatFileAttributes(ushort cx) {
        if (cx == 0) {
            return "0x0000 (none)";
        }
        List<string> flags = new List<string>();
        if ((cx & 0x01) != 0) {
            flags.Add("READ_ONLY");
        }
        if ((cx & 0x02) != 0) {
            flags.Add("HIDDEN");
        }
        if ((cx & 0x04) != 0) {
            flags.Add("SYSTEM");
        }
        if ((cx & 0x08) != 0) {
            flags.Add("VOLUME_LABEL");
        }
        if ((cx & 0x10) != 0) {
            flags.Add("DIRECTORY");
        }
        if ((cx & 0x20) != 0) {
            flags.Add("ARCHIVE");
        }
        if (flags.Count == 0) {
            return $"0x{cx:X4}";
        }
        return $"0x{cx:X4} = {string.Join(" | ", flags)}";
    }
}
