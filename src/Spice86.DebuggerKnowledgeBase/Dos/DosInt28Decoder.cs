namespace Spice86.DebuggerKnowledgeBase.Dos;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.DebuggerKnowledgeBase.Decoding;

/// <summary>
/// Decodes DOS INT 28h: DOS Idle. Issued by COMMAND.COM and other programs when waiting
/// for keyboard input to let TSRs perform background work; can be hooked to call
/// non-reentrant DOS functions (AH &lt; 0Dh) that would normally be unsafe from a TSR.
/// </summary>
public sealed class DosInt28Decoder : IInterruptDecoder {
    /// <inheritdoc />
    public bool CanDecode(byte vector) {
        return vector == 0x28;
    }

    /// <inheritdoc />
    public DecodedCall Decode(byte vector, State state, IMemory memory) {
        return new DecodedCall(
            "DOS INT 28h",
            "DOS Idle",
            "Signals DOS is idle waiting for input; TSRs hook this to use the safe-while-idle DOS functions.",
            [],
            []);
    }
}
