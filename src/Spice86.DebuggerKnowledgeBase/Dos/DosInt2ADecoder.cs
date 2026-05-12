namespace Spice86.DebuggerKnowledgeBase.Dos;

using System.Collections.Generic;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.DebuggerKnowledgeBase.Decoding;

/// <summary>
/// Decodes DOS INT 2Ah: Network and Critical Section services. AH selects the sub-function;
/// the most commonly used ones are AH=00h Network Installation Query and the critical-section
/// signaling pair AH=80h/81h used by DOS to bracket non-reentrant work.
/// </summary>
public sealed class DosInt2ADecoder : IInterruptDecoder {
    private const string Subsystem = "DOS INT 2Ah";

    private static readonly IReadOnlyDictionary<byte, string> FunctionNames = new Dictionary<byte, string> {
        [0x00] = "Network Installation Query",
        [0x01] = "Network Execute NetBIOS Request",
        [0x02] = "Set Network Printer Mode",
        [0x03] = "Get Network Printer Mode",
        [0x04] = "Submit Network Print Job",
        [0x05] = "Get Network Print Job Status",
        [0x06] = "Cancel Network Print Job",
        [0x80] = "Begin DOS Critical Section",
        [0x81] = "End DOS Critical Section",
        [0x82] = "End All DOS Critical Sections",
        [0x84] = "Keyboard Busy Loop"
    };

    /// <inheritdoc />
    public bool CanDecode(byte vector) {
        return vector == 0x2A;
    }

    /// <inheritdoc />
    public DecodedCall Decode(byte vector, State state, IMemory memory) {
        byte ah = state.AH;
        string functionName;
        if (FunctionNames.TryGetValue(ah, out string? name)) {
            functionName = $"AH={ah:X2}h {name}";
        } else {
            functionName = $"AH={ah:X2}h (unknown sub-function)";
        }
        List<DecodedParameter> parameters = [
            new DecodedParameter("sub-function", "AH", DecodedParameterKind.Register, ah, $"0x{ah:X2}", null)
        ];
        if (ah is 0x80 or 0x81) {
            byte sectionId = state.AL;
            parameters.Add(new DecodedParameter(
                "critical section id",
                "AL",
                DecodedParameterKind.Register,
                sectionId,
                $"0x{sectionId:X2}",
                "Section identifier; 1 = main DOS, 2 = DOS network, others reserved."));
        }
        return new DecodedCall(
            Subsystem,
            functionName,
            DescribeShort(ah),
            parameters,
            []);
    }

    private static string DescribeShort(byte ah) {
        return ah switch {
            0x00 => "Returns AH=0 if no network is installed, AH=FFh if a network redirector is loaded.",
            0x80 => "DOS marks entry to a critical region; TSRs must not invoke INT 21h while held.",
            0x81 => "Releases the matching DOS critical region.",
            0x82 => "Forcibly releases all DOS critical regions.",
            _ => "DOS Network and Critical Section services."
        };
    }
}
