namespace Spice86.DebuggerKnowledgeBase.Video;

using Spice86.DebuggerKnowledgeBase.Registries;

/// <summary>
/// One-stop registration helper for the Video knowledge base. Adds every video-related I/O port
/// decoder this project knows about to the given registry.
/// </summary>
public static class VideoDecoderRegistration {
    /// <summary>
    /// Registers all video I/O port decoders with the registry. Today this is the
    /// <see cref="VgaIoPortDecoder"/> covering the EGA/VGA port range
    /// (0x3B4..0x3BA, 0x3C0..0x3CF, 0x3D0..0x3DA).
    /// </summary>
    /// <param name="registry">Registry to populate.</param>
    public static void RegisterAll(IoPortDecoderRegistry registry) {
        registry.Register(new VgaIoPortDecoder());
    }
}
