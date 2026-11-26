namespace Spice86.Core.Emulator.Devices.Sound.Blaster;

/// <summary>
///     Which of the Sound Blaster's DMA callbacks is registered on one of its DMA channels.
/// </summary>
public enum SoundBlasterDmaCallback {
    /// <summary>None of the Sound Blaster's callbacks.</summary>
    None,

    /// <summary>The playback callback, which follows the channel's mask for the DSP's DMA transfer.</summary>
    Transfer,

    /// <summary>The DMA identification callback of DSP command E2h, which writes one byte when unmasked.</summary>
    DmaIdentification,

    /// <summary>The recording callback, which writes silence when unmasked and raises an IRQ.</summary>
    Recording
}
