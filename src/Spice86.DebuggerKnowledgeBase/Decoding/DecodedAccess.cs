namespace Spice86.DebuggerKnowledgeBase.Decoding;

/// <summary>
/// A decoded I/O port access. Wraps a <see cref="DecodedCall"/> with the access direction,
/// the port number, the value exchanged, and the access width in bytes.
/// </summary>
/// <param name="Port">I/O port number.</param>
/// <param name="Direction">Read or write.</param>
/// <param name="Width">Access width in bytes (1, 2 or 4).</param>
/// <param name="Value">Value read from or written to the port.</param>
/// <param name="Call">Decoded high-level meaning of the access.</param>
public sealed record DecodedAccess(
    ushort Port,
    IoPortAccessDirection Direction,
    int Width,
    uint Value,
    DecodedCall Call);
