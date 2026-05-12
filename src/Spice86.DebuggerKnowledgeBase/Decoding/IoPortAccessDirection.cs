namespace Spice86.DebuggerKnowledgeBase.Decoding;

/// <summary>
/// Whether a decoded I/O port access is a read or a write.
/// </summary>
public enum IoPortAccessDirection {
    /// <summary>Port read (IN instruction family).</summary>
    Read,

    /// <summary>Port write (OUT instruction family).</summary>
    Write
}
