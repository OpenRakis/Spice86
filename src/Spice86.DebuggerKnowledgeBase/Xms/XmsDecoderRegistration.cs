namespace Spice86.DebuggerKnowledgeBase.Xms;

/// <summary>
/// One-stop registration helper for the XMS knowledge base.
/// </summary>
/// <remarks>
/// XMS is invoked via a far call to the address returned by INT 2Fh AX=4310h, not via a
/// software interrupt or an I/O port. There is therefore no registry to populate from this
/// helper today; the decoder is exposed directly through <c>DebuggerDecoderService</c>. This
/// class exists so the wiring stays consistent with the other knowledge-base modules.
/// </remarks>
public static class XmsDecoderRegistration {
    /// <summary>
    /// Builds and returns the XMS call decoder. The caller is expected to plug it into the
    /// <c>DebuggerDecoderService</c>.
    /// </summary>
    public static XmsCallDecoder CreateDecoder() {
        return new XmsCallDecoder();
    }
}
