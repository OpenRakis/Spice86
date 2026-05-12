namespace Spice86.DebuggerKnowledgeBase.Bios;

using Spice86.DebuggerKnowledgeBase.Registries;

/// <summary>
/// One-stop registration helper for the BIOS knowledge base. Adds every BIOS-related interrupt
/// decoder this project knows about to the given registry.
/// </summary>
public static class BiosDecoderRegistration {
    /// <summary>
    /// Registers all BIOS interrupt decoders (INT 08h, 09h, 10h, 11h, 12h, 13h, 15h, 16h, 1Ah,
    /// 1Ch, 33h, 70h, 74h) with the registry. Mouse coverage spans BIOS-level (INT 15h AH=C2h
    /// pointing-device sub-functions in <see cref="BiosInt15Decoder"/> and INT 74h IRQ in
    /// <see cref="BiosMouseInt74Decoder"/>) and DOS driver-level (<see cref="BiosInt33Decoder"/>).
    /// </summary>
    /// <param name="registry">Registry to populate.</param>
    public static void RegisterAll(InterruptDecoderRegistry registry) {
        registry.Register(new BiosInt08Decoder());
        registry.Register(new BiosInt09Decoder());
        registry.Register(new BiosInt10Decoder());
        registry.Register(new BiosInt11Decoder());
        registry.Register(new BiosInt12Decoder());
        registry.Register(new BiosInt13Decoder());
        registry.Register(new BiosInt15Decoder());
        registry.Register(new BiosInt16Decoder());
        registry.Register(new BiosInt1ADecoder());
        registry.Register(new BiosInt1CDecoder());
        registry.Register(new BiosInt33Decoder());
        registry.Register(new BiosInt70Decoder());
        registry.Register(new BiosMouseInt74Decoder());
    }
}
