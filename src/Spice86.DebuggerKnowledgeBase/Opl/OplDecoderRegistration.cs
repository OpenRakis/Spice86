namespace Spice86.DebuggerKnowledgeBase.Opl;

using Spice86.DebuggerKnowledgeBase.Registries;

/// <summary>
/// One-stop registration helper for the OPL FM synthesizer knowledge base. Adds every
/// OPL-related I/O port decoder this project knows about to the given registry.
/// </summary>
public static class OplDecoderRegistration {
    /// <summary>
    /// Registers all OPL I/O port decoders with the registry. Today this is the
    /// <see cref="OplIoPortDecoder"/> covering the 4-port AdLib window 0x388..0x38B,
    /// which is shared by OPL2, Dual OPL2 (primary chip), OPL3 (both register arrays),
    /// and OPL3 Gold (OPL3 plus the AdLib Gold control unit / surround module).
    /// </summary>
    /// <param name="registry">Registry to populate.</param>
    public static void RegisterAll(IoPortDecoderRegistry registry) {
        registry.Register(new OplIoPortDecoder());
    }
}
