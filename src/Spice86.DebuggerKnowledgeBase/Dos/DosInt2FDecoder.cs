namespace Spice86.DebuggerKnowledgeBase.Dos;

using System.Collections.Generic;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.DebuggerKnowledgeBase.Decoding;

/// <summary>
/// Decodes DOS INT 2Fh multiplex calls. Mirrors the dispatch table in <c>DosInt2fHandler</c>:
/// the multiplex number is in AH, and AL selects a sub-function specific to that multiplex.
/// </summary>
public sealed class DosInt2FDecoder : IInterruptDecoder {
    private const string Subsystem = "DOS INT 2Fh";

    private static readonly IReadOnlyDictionary<byte, string> MultiplexNames = new Dictionary<byte, string> {
        [0x10] = "SHARE.EXE",
        [0x15] = "MSCDEX",
        [0x16] = "DOS Virtual Machine Services",
        [0x1A] = "ANSI.SYS Console Services",
        [0x43] = "XMS",
        [0x46] = "Windows Virtual Machine Services",
        [0x4A] = "High Memory Area Services"
    };

    /// <inheritdoc />
    public bool CanDecode(byte vector) {
        return vector == 0x2F;
    }

    /// <inheritdoc />
    public DecodedCall Decode(byte vector, State state, IMemory memory) {
        byte ah = state.AH;
        byte al = state.AL;
        string multiplex;
        if (MultiplexNames.TryGetValue(ah, out string? name)) {
            multiplex = name;
        } else {
            multiplex = $"AH={ah:X2}h (unknown multiplex)";
        }
        string functionName = $"AX={state.AX:X4}h {multiplex} sub-function {al:X2}h";
        string description = DescribeSubFunction(ah, al);
        IReadOnlyList<DecodedParameter> parameters = [
            new DecodedParameter("multiplex", "AH", DecodedParameterKind.Register, ah, $"0x{ah:X2} ({multiplex})", null),
            new DecodedParameter("sub-function", "AL", DecodedParameterKind.Register, al, $"0x{al:X2}", null)
        ];
        return new DecodedCall(Subsystem, functionName, description, parameters, []);
    }

    private static string DescribeSubFunction(byte ah, byte al) {
        if (ah == 0x43) {
            if (al == 0x00) {
                return "XMS Installation Check.";
            }
            if (al == 0x10) {
                return "Get XMS Driver Entry Point.";
            }
            return "XMS sub-function.";
        }
        if (ah == 0x4A) {
            if (al == 0x01) {
                return "Query Free HMA Space.";
            }
            if (al == 0x02) {
                return "Allocate HMA Space.";
            }
        }
        if (ah == 0x16 && al == 0x80) {
            return "Windows 3.0 Installation Check (undocumented).";
        }
        if (MultiplexNames.TryGetValue(ah, out string? name)) {
            return $"{name} sub-function.";
        }
        return "DOS multiplex sub-function.";
    }
}
