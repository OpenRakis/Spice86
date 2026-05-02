namespace Spice86.DebuggerKnowledgeBase.Dos;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.DebuggerKnowledgeBase.Decoding;

/// <summary>
/// Decodes DOS INT 20h: terminate the program and return control to the parent.
/// </summary>
public sealed class DosInt20Decoder : IInterruptDecoder {
    /// <inheritdoc />
    public bool CanDecode(byte vector) {
        return vector == 0x20;
    }

    /// <inheritdoc />
    public DecodedCall Decode(byte vector, State state, IMemory memory) {
        return new DecodedCall(
            "DOS INT 20h",
            "Terminate Program",
            "Exit to parent process; equivalent to INT 21h/AH=00h.",
            [],
            []);
    }
}
