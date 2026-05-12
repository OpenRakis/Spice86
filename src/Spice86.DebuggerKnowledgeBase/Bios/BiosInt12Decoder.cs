namespace Spice86.DebuggerKnowledgeBase.Bios;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.DebuggerKnowledgeBase.Decoding;

/// <summary>
/// Decodes BIOS INT 12h: get conventional memory size. Returns the size (KB) in AX.
/// </summary>
public sealed class BiosInt12Decoder : IInterruptDecoder {
    /// <inheritdoc />
    public bool CanDecode(byte vector) {
        return vector == 0x12;
    }

    /// <inheritdoc />
    public DecodedCall Decode(byte vector, State state, IMemory memory) {
        return new DecodedCall(
            "BIOS INT 12h",
            "Get Memory Size",
            "Return conventional memory size (KB) in AX.",
            [],
            []);
    }
}
