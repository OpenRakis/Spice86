namespace Spice86.DebuggerKnowledgeBase.Gus;

using Spice86.DebuggerKnowledgeBase.Registries;

/// <summary>
/// One-stop registration helper for the Gravis Ultrasound knowledge base. Registers
/// a <see cref="GusIoPortDecoder"/> at every standard GUS card base address so that
/// I/O probes during card detection and any actual GF1 traffic are decoded by name
/// regardless of which base the program targets.
/// </summary>
public static class GusDecoderRegistration {
    /// <summary>
    /// Registers a GUS I/O port decoder for every entry in
    /// <see cref="GusDecodingTables.StandardBases"/>.
    /// </summary>
    /// <param name="registry">Registry to populate.</param>
    public static void RegisterAll(IoPortDecoderRegistry registry) {
        foreach (ushort basePort in GusDecodingTables.StandardBases) {
            registry.Register(new GusIoPortDecoder(basePort));
        }
    }
}
