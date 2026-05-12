namespace Spice86.DebuggerKnowledgeBase.Bios;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.DebuggerKnowledgeBase.Decoding;

/// <summary>
/// Decodes BIOS INT 11h: get equipment list. Returns the equipment word in AX.
/// </summary>
public sealed class BiosInt11Decoder : IInterruptDecoder {
    /// <inheritdoc />
    public bool CanDecode(byte vector) {
        return vector == 0x11;
    }

    /// <inheritdoc />
    public DecodedCall Decode(byte vector, State state, IMemory memory) {
        return new DecodedCall(
            "BIOS INT 11h",
            "Get Equipment List",
            "Return the equipment word from BDA at 0040:0010 in AX.",
            [],
            []);
    }
}
