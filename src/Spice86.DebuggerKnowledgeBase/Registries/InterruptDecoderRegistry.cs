namespace Spice86.DebuggerKnowledgeBase.Registries;

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.DebuggerKnowledgeBase.Decoding;

/// <summary>
/// Holds the set of <see cref="IInterruptDecoder"/> instances registered with the debugger
/// and dispatches a (vector, state, memory) tuple to the first decoder that claims it.
/// </summary>
public sealed class InterruptDecoderRegistry {
    private readonly List<IInterruptDecoder> _decoders = new();

    /// <summary>
    /// Registers a new interrupt decoder. The order in which decoders are registered is the
    /// order in which they are queried; the first one whose <see cref="IInterruptDecoder.CanDecode"/>
    /// returns true wins.
    /// </summary>
    /// <param name="decoder">Decoder to register.</param>
    public void Register(IInterruptDecoder decoder) {
        _decoders.Add(decoder);
    }

    /// <summary>
    /// Tries to decode an interrupt invocation.
    /// </summary>
    /// <param name="vector">Interrupt vector number.</param>
    /// <param name="state">Current CPU state.</param>
    /// <param name="memory">Emulated memory bus.</param>
    /// <param name="call">Decoded call when the method returns true; null otherwise.</param>
    /// <returns>True when a decoder produced a decoded call.</returns>
    public bool TryDecode(byte vector, State state, IMemory memory, [NotNullWhen(true)] out DecodedCall? call) {
        foreach (IInterruptDecoder decoder in _decoders) {
            if (decoder.CanDecode(vector)) {
                call = decoder.Decode(vector, state, memory);
                return true;
            }
        }
        call = null;
        return false;
    }
}
