namespace Spice86.Core.Emulator.Devices.Sound.Blaster;

using System.Collections.Immutable;

/// <summary>
///     State of the Sound Blaster that a program can observe through its ports, its IRQ and its DMA transfers, as
///     captured by <see cref="SoundBlaster.CaptureState" />.
/// </summary>
/// <remarks>
///     The IRQ line latched in the interrupt controller and the DMA channels' registers belong to those devices'
///     states. The host side of the audio output is not held: frames already queued for the mixer, the mixer
///     channel's sample rate, volumes, filters and sleep state, and the measured direct DAC write rate.
/// </remarks>
/// <param name="Dsp">The DSP's command interface.</param>
/// <param name="Dma">The DSP's DMA transfer.</param>
/// <param name="Mixer">The mixer chip's registers.</param>
/// <param name="Mode">What the DSP outputs.</param>
/// <param name="FrequencyHz">Sample rate set by the time constant or sample rate commands.</param>
/// <param name="SpeakerEnabled">Whether the speaker is on.</param>
/// <param name="PendingIrq8Bit">Whether an 8-bit IRQ waits for its acknowledge on the read status port.</param>
/// <param name="PendingIrq16Bit">Whether a 16-bit IRQ waits for its acknowledge on port 0Fh.</param>
/// <param name="AdpcmReference">Last decoded ADPCM sample.</param>
/// <param name="AdpcmStepSize">Current ADPCM step size.</param>
/// <param name="AdpcmHasReference">Whether the next ADPCM block starts with a reference byte.</param>
/// <param name="DmaIdentificationValue">Value DSP command E2h computes and writes through DMA.</param>
/// <param name="DmaIdentificationCount">Number of E2h commands, which selects the next increment table.</param>
/// <param name="AspRegisters">The 256 registers of the SB16 advanced signal processor.</param>
/// <param name="AspInitInProgress">Whether the ASP initialization mode is on.</param>
/// <param name="LowDmaCallback">The Sound Blaster's callback registered on its 8-bit DMA channel.</param>
/// <param name="HighDmaCallback">
///     The Sound Blaster's callback registered on its 16-bit DMA channel, <see cref="SoundBlasterDmaCallback.None" />
///     when the card does not use one.
/// </param>
/// <param name="Timing">Which scheduler callback generates output frames, which reads the DMA transfer.</param>
/// <param name="FrameCounter">Fractional frames carried over between per-tick callbacks.</param>
/// <param name="FramesAddedThisTick">Frames generated in the current tick.</param>
/// <param name="LastDmaCallbackTimeMs">Time on the emulated clock of the last DMA block read.</param>
/// <param name="CapturedAtMs">Elapsed milliseconds on the emulated clock at capture.</param>
/// <param name="Events">The Sound Blaster's queued scheduler events, earliest first.</param>
public sealed record SoundBlasterState(
    SoundBlasterDspState Dsp,
    SoundBlasterDmaTransferState Dma,
    SoundBlasterMixerState Mixer,
    SoundBlaster.DspMode Mode,
    uint FrequencyHz,
    bool SpeakerEnabled,
    bool PendingIrq8Bit,
    bool PendingIrq16Bit,
    byte AdpcmReference,
    ushort AdpcmStepSize,
    bool AdpcmHasReference,
    int DmaIdentificationValue,
    uint DmaIdentificationCount,
    ReadOnlyMemory<byte> AspRegisters,
    bool AspInitInProgress,
    SoundBlasterDmaCallback LowDmaCallback,
    SoundBlasterDmaCallback HighDmaCallback,
    SoundBlaster.TimingType Timing,
    float FrameCounter,
    int FramesAddedThisTick,
    double LastDmaCallbackTimeMs,
    double CapturedAtMs,
    ImmutableArray<SoundBlasterScheduledEvent> Events);
