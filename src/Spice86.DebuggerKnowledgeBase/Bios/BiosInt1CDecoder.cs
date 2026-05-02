namespace Spice86.DebuggerKnowledgeBase.Bios;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.DebuggerKnowledgeBase.Decoding;

/// <summary>
/// Decodes BIOS INT 1Ch: user timer tick hook. Called from INT 08h on every tick. Default
/// handler is an IRET, but applications hook it for periodic work.
/// </summary>
public sealed class BiosInt1CDecoder : IInterruptDecoder {
    /// <inheritdoc />
    public bool CanDecode(byte vector) {
        return vector == 0x1C;
    }

    /// <inheritdoc />
    public DecodedCall Decode(byte vector, State state, IMemory memory) {
        return new DecodedCall(
            "BIOS INT 1Ch",
            "User Timer Tick Hook",
            "Called from INT 08h on every tick (default = IRET); applications hook this for periodic work.",
            [],
            []);
    }
}
