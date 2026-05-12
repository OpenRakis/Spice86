namespace Spice86.DebuggerKnowledgeBase.Registries;

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

using Spice86.DebuggerKnowledgeBase.Decoding;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Holds the set of <see cref="IAsmRoutineDecoder"/> instances registered with the debugger.
/// </summary>
public sealed class AsmRoutineDecoderRegistry {
    private readonly List<IAsmRoutineDecoder> _decoders = new();

    /// <summary>
    /// Registers a new ASM routine decoder.
    /// </summary>
    /// <param name="decoder">Decoder to register.</param>
    public void Register(IAsmRoutineDecoder decoder) {
        _decoders.Add(decoder);
    }

    /// <summary>
    /// Tries to decode an emulator-installed ASM routine entry point.
    /// </summary>
    /// <param name="entryPoint">Routine entry point address.</param>
    /// <param name="call">Decoded call when the method returns true; null otherwise.</param>
    public bool TryDecode(SegmentedAddress entryPoint, [NotNullWhen(true)] out DecodedCall? call) {
        foreach (IAsmRoutineDecoder decoder in _decoders) {
            if (decoder.CanDecode(entryPoint)) {
                call = decoder.Decode(entryPoint);
                return true;
            }
        }
        call = null;
        return false;
    }
}
