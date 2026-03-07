namespace Spice86.Core.Emulator.Devices.Sound.Blaster;

using System;

/// <summary>
/// Represents the interrupt status register for the SoundBlaster hardware mixer.
/// </summary>
[Flags]
public enum InterruptStatus : byte {
    /// <summary>
    /// No interrupts pending.
    /// </summary>
    None = 0x00,

    /// <summary>
    /// 8-bit DMA interrupt pending (bit 0).
    /// </summary>
    Dma8Bit = 0x01,

    /// <summary>
    /// 16-bit DMA interrupt pending (bit 1).
    /// </summary>
    Dma16Bit = 0x02,

    /// <summary>
    /// MPU-401 MIDI interrupt pending (bit 2).
    /// </summary>
    Mpu401 = 0x04
}