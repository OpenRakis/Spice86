namespace Spice86.DebuggerKnowledgeBase.Dos;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.DebuggerKnowledgeBase.Decoding;

/// <summary>
/// Decodes DOS INT 23h: Control-Break Handler. Saved at PSP:0Eh. DOS calls this vector
/// when the user presses Ctrl+C / Ctrl+Break during a character I/O check; returning with
/// CF set asks DOS to terminate the program with Ctrl+C status, CF clear continues.
/// </summary>
public sealed class DosInt23Decoder : IInterruptDecoder {
    /// <inheritdoc />
    public bool CanDecode(byte vector) {
        return vector == 0x23;
    }

    /// <inheritdoc />
    public DecodedCall Decode(byte vector, State state, IMemory memory) {
        return new DecodedCall(
            "DOS INT 23h",
            "Control-Break Handler",
            "Invoked by DOS when Ctrl+C / Ctrl+Break is detected; RETF with CF set terminates the program.",
            [],
            []);
    }
}
