namespace Spice86.DebuggerKnowledgeBase.Joystick;

using Spice86.DebuggerKnowledgeBase.Registries;

/// <summary>
/// One-stop registration helper for the joystick gameport knowledge base. Registers
/// a <see cref="JoystickIoPortDecoder"/> covering the full 0x200..0x207 gameport
/// address window so both canonical accesses to 0x201 and partial-decode aliases
/// are decoded by name.
/// </summary>
public static class JoystickDecoderRegistration {
    /// <summary>
    /// Registers a single <see cref="JoystickIoPortDecoder"/> for the gameport.
    /// </summary>
    /// <param name="registry">Registry to populate.</param>
    public static void RegisterAll(IoPortDecoderRegistry registry) {
        registry.Register(new JoystickIoPortDecoder());
    }
}
