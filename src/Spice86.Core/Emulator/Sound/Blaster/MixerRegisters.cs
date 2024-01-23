namespace Spice86.Core.Emulator.Sound.Blaster;

/// <summary>
/// This class contains the register addresses for the SoundBlaster mixer.
/// </summary>
public static class MixerRegisters {
    /// <summary>
    /// The register address for the IRQ (interrupt request) bit.
    /// </summary>
    public const byte IRQ = 0x80;

    /// <summary>
    /// The register address for the DMA (direct memory access) bit.
    /// </summary>
    public const byte DMA = 0x81;

    /// <summary>
    /// The register address for the interrupt status.
    /// </summary>
    public const byte InterruptStatus = 0x82;
}