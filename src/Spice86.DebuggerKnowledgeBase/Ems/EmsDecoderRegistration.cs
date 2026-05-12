namespace Spice86.DebuggerKnowledgeBase.Ems;

using Spice86.DebuggerKnowledgeBase.Registries;

/// <summary>
/// One-stop registration helper for the EMS knowledge base.
/// </summary>
public static class EmsDecoderRegistration {
    /// <summary>
    /// Registers the EMS INT 67h decoder with the given registry.
    /// </summary>
    /// <param name="registry">Registry to populate.</param>
    public static void RegisterAll(InterruptDecoderRegistry registry) {
        registry.Register(new EmsInt67Decoder());
    }
}
