namespace Spice86.DebuggerKnowledgeBase.Bios;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.DebuggerKnowledgeBase.Decoding;

/// <summary>
/// Decodes BIOS INT 70h: real-time clock hardware interrupt (IRQ 8).
/// </summary>
public sealed class BiosInt70Decoder : IInterruptDecoder {
    /// <inheritdoc />
    public bool CanDecode(byte vector) {
        return vector == 0x70;
    }

    /// <inheritdoc />
    public DecodedCall Decode(byte vector, State state, IMemory memory) {
        return new DecodedCall(
            "BIOS INT 70h",
            "RTC IRQ (IRQ 8)",
            "Real-time clock hardware interrupt; services the periodic, alarm, or update-ended interrupts from the CMOS RTC.",
            [],
            []);
    }
}
