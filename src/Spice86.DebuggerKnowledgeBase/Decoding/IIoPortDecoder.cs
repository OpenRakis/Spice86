namespace Spice86.DebuggerKnowledgeBase.Decoding;

/// <summary>
/// Decodes I/O port reads and writes into high-level <see cref="DecodedCall"/> values.
/// Implementations must be pure: they take the port number and the value, and never touch
/// emulator state.
/// </summary>
public interface IIoPortDecoder {
    /// <summary>
    /// Returns true when this decoder claims the given port.
    /// </summary>
    /// <param name="port">I/O port number.</param>
    bool CanDecode(ushort port);

    /// <summary>
    /// Decodes a port read.
    /// </summary>
    /// <param name="port">I/O port number.</param>
    /// <param name="value">Value that was read.</param>
    /// <param name="width">Access width in bytes (1, 2 or 4).</param>
    DecodedCall DecodeRead(ushort port, uint value, int width);

    /// <summary>
    /// Decodes a port write.
    /// </summary>
    /// <param name="port">I/O port number.</param>
    /// <param name="value">Value that was written.</param>
    /// <param name="width">Access width in bytes (1, 2 or 4).</param>
    DecodedCall DecodeWrite(ushort port, uint value, int width);
}
