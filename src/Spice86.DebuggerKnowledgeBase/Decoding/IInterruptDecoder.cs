namespace Spice86.DebuggerKnowledgeBase.Decoding;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;

/// <summary>
/// Decodes a software interrupt invocation into a high-level <see cref="DecodedCall"/>.
/// Implementations must be pure: they read CPU state and memory but never mutate them.
/// </summary>
public interface IInterruptDecoder {
    /// <summary>
    /// Returns true when this decoder knows how to decode the given interrupt vector.
    /// </summary>
    /// <param name="vector">Interrupt vector number (0..255).</param>
    bool CanDecode(byte vector);

    /// <summary>
    /// Decodes the interrupt invocation as currently set up in the CPU state.
    /// </summary>
    /// <param name="vector">Interrupt vector number (0..255).</param>
    /// <param name="state">Current CPU state.</param>
    /// <param name="memory">Emulated memory bus.</param>
    DecodedCall Decode(byte vector, State state, IMemory memory);
}
