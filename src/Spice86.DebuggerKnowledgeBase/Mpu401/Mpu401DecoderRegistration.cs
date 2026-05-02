namespace Spice86.DebuggerKnowledgeBase.Mpu401;

using Spice86.DebuggerKnowledgeBase.Registries;

/// <summary>
/// One-stop registration helper for the MPU-401 (General MIDI / MT-32) knowledge base.
/// Adds every MPU-401 related I/O port decoder this project knows about to the given
/// registry.
/// </summary>
public static class Mpu401DecoderRegistration {
    /// <summary>
    /// Registers all MPU-401 I/O port decoders with the registry. Today this is the
    /// <see cref="Mpu401IoPortDecoder"/> covering the 2-port window at every standard
    /// MPU-401 base address (0x300 / 0x310 / 0x320 / 0x330 / 0x332 / 0x334 / 0x336 /
    /// 0x338 / 0x340 / 0x350 / 0x360). The same decoder applies whether the connected
    /// synth is a Roland General MIDI device or a Roland MT-32 (incl. CM-32L / CM-64),
    /// since both expose the MPU-401 register interface and only differ in the MIDI
    /// payload semantics.
    /// </summary>
    /// <param name="registry">Registry to populate.</param>
    public static void RegisterAll(IoPortDecoderRegistry registry) {
        registry.Register(new Mpu401IoPortDecoder());
    }
}
