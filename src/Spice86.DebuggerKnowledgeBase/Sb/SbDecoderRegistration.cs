namespace Spice86.DebuggerKnowledgeBase.Sb;

using Spice86.DebuggerKnowledgeBase.Registries;

/// <summary>
/// One-stop registration helper for the Sound Blaster knowledge base. Adds every SB-related
/// I/O port decoder this project knows about to the given registry.
/// </summary>
public static class SbDecoderRegistration {
    /// <summary>
    /// Registers all Sound Blaster I/O port decoders with the registry. Today this is the
    /// <see cref="SbIoPortDecoder"/> covering the 16-port window at every standard SB base
    /// address (0x210 / 0x220 / 0x230 / 0x240 / 0x250 / 0x260 / 0x280) for SB1, SB2, SB Pro 1,
    /// SB Pro 2 (including ESS-flavoured variants), SB16, and Game Blaster (CMS).
    /// </summary>
    /// <param name="registry">Registry to populate.</param>
    public static void RegisterAll(IoPortDecoderRegistry registry) {
        registry.Register(new SbIoPortDecoder());
    }
}
