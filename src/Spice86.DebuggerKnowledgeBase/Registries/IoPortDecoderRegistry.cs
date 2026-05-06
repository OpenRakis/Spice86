namespace Spice86.DebuggerKnowledgeBase.Registries;

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

using Spice86.DebuggerKnowledgeBase.Decoding;

/// <summary>
/// Holds the set of <see cref="IIoPortDecoder"/> instances registered with the debugger
/// and dispatches port reads/writes to the first decoder that claims the port.
/// </summary>
public sealed class IoPortDecoderRegistry {
    private readonly List<IIoPortDecoder> _decoders = new();

    /// <summary>
    /// Registers a new I/O port decoder.
    /// </summary>
    /// <param name="decoder">Decoder to register.</param>
    public void Register(IIoPortDecoder decoder) {
        _decoders.Add(decoder);
    }

    /// <summary>
    /// Tries to decode a port read.
    /// </summary>
    /// <param name="port">I/O port number.</param>
    /// <param name="value">Value that was read.</param>
    /// <param name="width">Access width in bytes (1, 2 or 4).</param>
    /// <param name="call">Decoded call when the method returns true; null otherwise.</param>
    public bool TryDecodeRead(ushort port, uint value, int width, [NotNullWhen(true)] out DecodedCall? call) {
        foreach (IIoPortDecoder decoder in _decoders) {
            if (decoder.CanDecode(port)) {
                call = decoder.DecodeRead(port, value, width);
                return true;
            }
        }
        call = null;
        return false;
    }

    /// <summary>
    /// Tries to decode a port write.
    /// </summary>
    /// <param name="port">I/O port number.</param>
    /// <param name="value">Value that was written.</param>
    /// <param name="width">Access width in bytes (1, 2 or 4).</param>
    /// <param name="call">Decoded call when the method returns true; null otherwise.</param>
    public bool TryDecodeWrite(ushort port, uint value, int width, [NotNullWhen(true)] out DecodedCall? call) {
        foreach (IIoPortDecoder decoder in _decoders) {
            if (decoder.CanDecode(port)) {
                call = decoder.DecodeWrite(port, value, width);
                return true;
            }
        }
        call = null;
        return false;
    }
}
