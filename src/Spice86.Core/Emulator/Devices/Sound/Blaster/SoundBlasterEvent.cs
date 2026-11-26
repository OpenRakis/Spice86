namespace Spice86.Core.Emulator.Devices.Sound.Blaster;

/// <summary>
///     Kinds of scheduler events the Sound Blaster queues.
/// </summary>
public enum SoundBlasterEvent {
    /// <summary>The end of a DSP reset, which puts AAh in the output buffer.</summary>
    FinishReset,

    /// <summary>An 8-bit IRQ, after a silence block or DSP command F2h.</summary>
    RaiseIrq,

    /// <summary>The end of a DMA block shorter than three milliseconds.</summary>
    ProcessDmaTransfer,

    /// <summary>A DMA read while the speaker is off, which reads the samples without playing them.</summary>
    SuppressDmaTransfer,

    /// <summary>Output generation every millisecond.</summary>
    PerTick,

    /// <summary>Output generation every output frame.</summary>
    PerFrame
}
