namespace Spice86.DebuggerKnowledgeBase.Bios;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.DebuggerKnowledgeBase.Decoding;

/// <summary>
/// Decodes BIOS INT 09h: keyboard hardware interrupt (IRQ 1).
/// </summary>
public sealed class BiosInt09Decoder : IInterruptDecoder {
    /// <inheritdoc />
    public bool CanDecode(byte vector) {
        return vector == 0x09;
    }

    /// <inheritdoc />
    public DecodedCall Decode(byte vector, State state, IMemory memory) {
        return new DecodedCall(
            "BIOS INT 09h",
            "Keyboard IRQ (IRQ 1)",
            "Hardware keyboard interrupt; reads scan code from port 0x60 and pushes it into the BIOS keyboard buffer.",
            [],
            []);
    }
}
