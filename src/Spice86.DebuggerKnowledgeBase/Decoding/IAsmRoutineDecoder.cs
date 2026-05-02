namespace Spice86.DebuggerKnowledgeBase.Decoding;

using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Decodes the entry point of an emulator-installed ASM routine (the IRET-trampoline produced
/// by <c>MemoryAsmWriter</c>) into a high-level <see cref="DecodedCall"/> describing what the
/// emulator is going to do when execution lands on that address.
/// </summary>
public interface IAsmRoutineDecoder {
    /// <summary>
    /// Returns true when this decoder knows about the routine starting at the given address.
    /// </summary>
    /// <param name="entryPoint">Routine entry point.</param>
    bool CanDecode(SegmentedAddress entryPoint);

    /// <summary>
    /// Decodes the routine at the given entry point.
    /// </summary>
    /// <param name="entryPoint">Routine entry point.</param>
    DecodedCall Decode(SegmentedAddress entryPoint);
}
