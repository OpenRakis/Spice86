namespace Spice86.DebuggerKnowledgeBase.Dos;

using Spice86.DebuggerKnowledgeBase.Registries;

/// <summary>
/// One-stop registration helper for the DOS knowledge base. Adds every DOS-related interrupt
/// decoder this project knows about to the given registry.
/// </summary>
public static class DosDecoderRegistration {
    /// <summary>
    /// Registers all DOS interrupt decoders (INT 20h, INT 21h, INT 2Fh) with the registry.
    /// </summary>
    /// <param name="registry">Registry to populate.</param>
    public static void RegisterAll(InterruptDecoderRegistry registry) {
        registry.Register(new DosInt20Decoder());
        registry.Register(new DosInt21Decoder());
        registry.Register(new DosInt2FDecoder());
    }
}
