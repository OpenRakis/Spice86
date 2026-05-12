namespace Spice86.DebuggerKnowledgeBase.Bios;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.DebuggerKnowledgeBase.Decoding;

/// <summary>
/// Decodes BIOS INT 08h: timer tick (IRQ 0). Increments the BDA tick counter and chains to INT 1Ch.
/// </summary>
public sealed class BiosInt08Decoder : IInterruptDecoder {
    /// <inheritdoc />
    public bool CanDecode(byte vector) {
        return vector == 0x08;
    }

    /// <inheritdoc />
    public DecodedCall Decode(byte vector, State state, IMemory memory) {
        return new DecodedCall(
            "BIOS INT 08h",
            "Timer Tick (IRQ 0)",
            "Hardware timer interrupt; increments BDA tick counter at 0040:006C and chains INT 1Ch.",
            [],
            []);
    }
}
