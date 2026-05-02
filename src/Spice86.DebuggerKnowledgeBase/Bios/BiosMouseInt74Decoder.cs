namespace Spice86.DebuggerKnowledgeBase.Bios;

using System.Collections.Generic;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.DebuggerKnowledgeBase.Decoding;

/// <summary>
/// Decodes BIOS INT 74h (PS/2 mouse hardware IRQ 12). Mirrors <c>BiosMouseInt74Handler</c>. This
/// is the BIOS-level mouse interface: invoked by the PS/2 controller through the slave PIC. It
/// reads the mouse data packet from port 60h and forwards it to the user handler installed via
/// INT 15h AH=C2h AL=07h. The DOS driver-level interface lives at INT 33h.
/// </summary>
public sealed class BiosMouseInt74Decoder : IInterruptDecoder {
    private const string Subsystem = "BIOS INT 74h";

    /// <inheritdoc />
    public bool CanDecode(byte vector) {
        return vector == 0x74;
    }

    /// <inheritdoc />
    public DecodedCall Decode(byte vector, State state, IMemory memory) {
        return new DecodedCall(
            Subsystem,
            "Mouse Hardware IRQ (PS/2)",
            "BIOS-level PS/2 mouse interrupt: reads the next mouse packet and dispatches it to the handler installed via INT 15h AH=C2h AL=07h.",
            [],
            []);
    }
}
