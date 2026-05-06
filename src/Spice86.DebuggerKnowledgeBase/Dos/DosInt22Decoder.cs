namespace Spice86.DebuggerKnowledgeBase.Dos;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.DebuggerKnowledgeBase.Decoding;

/// <summary>
/// Decodes DOS INT 22h: Terminate Address. The far pointer to this handler is copied
/// into the child PSP (offset 0Ah) by EXEC; when the child exits, DOS jumps here so the
/// parent regains control. Invoking it directly terminates the current process normally.
/// </summary>
public sealed class DosInt22Decoder : IInterruptDecoder {
    /// <inheritdoc />
    public bool CanDecode(byte vector) {
        return vector == 0x22;
    }

    /// <inheritdoc />
    public DecodedCall Decode(byte vector, State state, IMemory memory) {
        return new DecodedCall(
            "DOS INT 22h",
            "Terminate Address",
            "Parent's resume vector saved at PSP:0Ah; jumped to when the child process exits.",
            [],
            []);
    }
}
