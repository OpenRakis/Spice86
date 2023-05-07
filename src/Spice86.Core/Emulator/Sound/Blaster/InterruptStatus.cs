namespace Spice86.Core.Emulator.Sound.Blaster;

using System;

/// <summary>
/// Represents the contents of the Interrupt Status register.
/// </summary>
[Flags]
public enum InterruptStatus {
    /// <summary>
    /// The register is clear.
    /// </summary>
    None = 0,
    /// <summary>
    /// An 8-bit DMA IRQ occurred.
    /// </summary>
    Dma8 = 1,
    /// <summary>
    /// A 16-bit DMA IRQ occurred.
    /// </summary>
    Dma16 = 2,
    /// <summary>
    /// An MPU-401 IRQ occurred.
    /// </summary>
    Mpu401 = 4
}
