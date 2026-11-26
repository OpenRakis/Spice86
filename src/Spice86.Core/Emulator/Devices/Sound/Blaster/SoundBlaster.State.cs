namespace Spice86.Core.Emulator.Devices.Sound.Blaster;

using Spice86.Core.Emulator.Devices.DirectMemoryAccess;
using Spice86.Core.Emulator.VM.DeviceScheduler;

using System.Collections.Immutable;

using EventHandler = Spice86.Core.Emulator.VM.DeviceScheduler.EventHandler;

public partial class SoundBlaster {
    private static readonly SoundBlasterEvent[] OwnEvents = Enum.GetValues<SoundBlasterEvent>();

    private static readonly SoundBlasterDmaCallback[] OwnDmaCallbacks = [
        SoundBlasterDmaCallback.Transfer, SoundBlasterDmaCallback.DmaIdentification, SoundBlasterDmaCallback.Recording
    ];

    /// <summary>
    ///     Captures what a program can observe of the card: the DSP's command interface and buffers, the DMA
    ///     transfer, the mixer registers, the pending IRQ flags, the DMA callbacks and the queued scheduler events.
    /// </summary>
    /// <returns>The state of the card.</returns>
    public SoundBlasterState CaptureState() {
        double now = _clock.ElapsedTimeMs;
        SoundBlasterDmaCallback highDmaCallback = ShouldUseHighDmaChannel()
            ? CaptureDmaCallback(_sb.Hw.Dma16)
            : SoundBlasterDmaCallback.None;
        return new SoundBlasterState(
            CaptureDspState(),
            CaptureDmaTransferState(),
            CaptureMixerState(),
            _sb.Mode,
            _sb.FreqHz,
            _sb.SpeakerEnabled,
            _sb.Irq.Pending8Bit,
            _sb.Irq.Pending16Bit,
            _sb.Adpcm.Reference,
            _sb.Adpcm.Stepsize,
            _sb.Adpcm.HaveRef,
            _sb.E2.Value,
            _sb.E2.Count,
            _aspRegs.AsSpan().ToArray(),
            _aspInitInProgress,
            CaptureDmaCallback(_sb.Hw.Dma8),
            highDmaCallback,
            _timingType,
            _frameCounter,
            _framesAddedThisTick,
            _lastDmaCallbackTime,
            now,
            CaptureEvents(now));
    }

    /// <summary>
    ///     Puts the card back in a captured state.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The Sound Blaster's scheduler events are removed and queued again as far ahead as they were at
    ///         capture, and the time of the last DMA block read moves by the time that passed since. The Sound
    ///         Blaster's callbacks are put back on its DMA channels without calling them and without changing the
    ///         channels' registers; restore the DMA controllers' state for those.
    ///     </para>
    ///     <para>
    ///         The IRQ line is not touched: restore the interrupt controller's state for that. The host audio
    ///         output is left as it is: frames already queued for the mixer stay queued, and the mixer channel
    ///         keeps its sample rate, volumes and filter.
    ///     </para>
    /// </remarks>
    /// <param name="state">State captured by <see cref="CaptureState" />.</param>
    public void RestoreState(SoundBlasterState state) {
        double elapsedSinceCapture = _clock.ElapsedTimeMs - state.CapturedAtMs;
        RestoreDspState(state.Dsp);
        RestoreDmaTransferState(state.Dma);
        RestoreMixerState(state.Mixer);
        _sb.Mode = state.Mode;
        _sb.FreqHz = state.FrequencyHz;
        _sb.SpeakerEnabled = state.SpeakerEnabled;
        _sb.Irq.Pending8Bit = state.PendingIrq8Bit;
        _sb.Irq.Pending16Bit = state.PendingIrq16Bit;
        _sb.Adpcm.Reference = state.AdpcmReference;
        _sb.Adpcm.Stepsize = state.AdpcmStepSize;
        _sb.Adpcm.HaveRef = state.AdpcmHasReference;
        _sb.E2.Value = state.DmaIdentificationValue;
        _sb.E2.Count = state.DmaIdentificationCount;
        state.AspRegisters.Span.CopyTo(_aspRegs);
        _aspInitInProgress = state.AspInitInProgress;
        RestoreDmaCallback(_sb.Hw.Dma8, state.LowDmaCallback);
        if (ShouldUseHighDmaChannel()) {
            RestoreDmaCallback(_sb.Hw.Dma16, state.HighDmaCallback);
        }
        _timingType = state.Timing;
        _frameCounter = state.FrameCounter;
        _framesAddedThisTick = state.FramesAddedThisTick;
        _lastDmaCallbackTime = state.LastDmaCallbackTimeMs + elapsedSinceCapture;
        RestoreEvents(state.Events);
    }

    private SoundBlasterDspState CaptureDspState() {
        SbInfo.DspStateInfo dsp = _sb.Dsp;
        return new SoundBlasterDspState(
            dsp.State,
            dsp.Cmd,
            dsp.CmdLen,
            dsp.In.Data.AsSpan().ToArray(),
            dsp.In.Pos,
            dsp.Out.Data.AsSpan().ToArray(),
            dsp.Out.Pos,
            dsp.Out.Used,
            dsp.Out.LastVal,
            dsp.TestRegister,
            dsp.WriteStatusCounter,
            dsp.ResetTally,
            dsp.WarmupRemainingMs);
    }

    private void RestoreDspState(SoundBlasterDspState state) {
        SbInfo.DspStateInfo dsp = _sb.Dsp;
        dsp.State = state.State;
        dsp.Cmd = state.Command;
        dsp.CmdLen = state.CommandLength;
        state.InputData.Span.CopyTo(dsp.In.Data);
        dsp.In.Pos = state.InputPosition;
        state.OutputData.Span.CopyTo(dsp.Out.Data);
        dsp.Out.Pos = state.OutputPosition;
        dsp.Out.Used = state.OutputUsed;
        dsp.Out.LastVal = state.OutputLastValue;
        dsp.TestRegister = state.TestRegister;
        dsp.WriteStatusCounter = state.WriteStatusCounter;
        dsp.ResetTally = state.ResetTally;
        dsp.WarmupRemainingMs = state.WarmupRemainingMs;
    }

    private SoundBlasterDmaTransferState CaptureDmaTransferState() {
        SbInfo.DmaState dma = _sb.Dma;
        byte? channelNumber = dma.Channel?.ChannelNumber;
        return new SoundBlasterDmaTransferState(
            dma.Mode,
            channelNumber,
            dma.Stereo,
            dma.Sign,
            dma.AutoInit,
            dma.FirstTransfer,
            dma.Rate,
            dma.Mul,
            dma.SingleSize,
            dma.AutoSize,
            dma.Left,
            dma.Min,
            dma.RemainSize,
            dma.Buf8[0],
            dma.Buf16[0]);
    }

    private void RestoreDmaTransferState(SoundBlasterDmaTransferState state) {
        SbInfo.DmaState dma = _sb.Dma;
        dma.Mode = state.Mode;
        dma.Channel = state.ChannelNumber is { } channelNumber ? _dmaBus.GetChannel(channelNumber) : null;
        dma.Stereo = state.Stereo;
        dma.Sign = state.Signed;
        dma.AutoInit = state.AutoInit;
        dma.FirstTransfer = state.FirstTransfer;
        dma.Rate = state.Rate;
        dma.Mul = state.Multiplier;
        dma.SingleSize = state.SingleSize;
        dma.AutoSize = state.AutoSize;
        dma.Left = state.Left;
        dma.Min = state.Min;
        dma.RemainSize = state.RemainSize;
        dma.Buf8[0] = state.PendingSample8Bit;
        dma.Buf16[0] = state.PendingSample16Bit;
    }

    private SoundBlasterMixerState CaptureMixerState() {
        SbInfo.MixerState mixer = _sb.Mixer;
        return new SoundBlasterMixerState(
            mixer.Index,
            mixer.Dac.AsSpan().ToArray(),
            mixer.Fm.AsSpan().ToArray(),
            mixer.Cda.AsSpan().ToArray(),
            mixer.Master.AsSpan().ToArray(),
            mixer.Lin.AsSpan().ToArray(),
            mixer.Mic,
            mixer.StereoEnabled,
            mixer.FilterEnabled,
            mixer.Unhandled.AsSpan().ToArray(),
            mixer.EssIdStrPos);
    }

    private void RestoreMixerState(SoundBlasterMixerState state) {
        SbInfo.MixerState mixer = _sb.Mixer;
        mixer.Index = state.Index;
        state.Dac.Span.CopyTo(mixer.Dac);
        state.Fm.Span.CopyTo(mixer.Fm);
        state.CdAudio.Span.CopyTo(mixer.Cda);
        state.Master.Span.CopyTo(mixer.Master);
        state.LineIn.Span.CopyTo(mixer.Lin);
        mixer.Mic = state.Mic;
        mixer.StereoEnabled = state.StereoEnabled;
        mixer.FilterEnabled = state.FilterEnabled;
        state.Unhandled.Span.CopyTo(mixer.Unhandled);
        mixer.EssIdStrPos = state.EssIdentificationPosition;
    }

    private SoundBlasterDmaCallback CaptureDmaCallback(byte channelNumber) {
        DmaChannel.DmaCallback? callback = _dmaBus.GetChannel(channelNumber)?.Callback;
        if (callback is null) {
            return SoundBlasterDmaCallback.None;
        }
        foreach (SoundBlasterDmaCallback kind in OwnDmaCallbacks) {
            if (callback.Equals(GetDmaCallback(kind))) {
                return kind;
            }
        }
        return SoundBlasterDmaCallback.None;
    }

    private void RestoreDmaCallback(byte channelNumber, SoundBlasterDmaCallback kind) {
        DmaChannel? channel = _dmaBus.GetChannel(channelNumber);
        if (channel is null) {
            return;
        }
        bool channelHasOtherDevicesCallback = channel.Callback is not null
            && CaptureDmaCallback(channelNumber) == SoundBlasterDmaCallback.None;
        if (channelHasOtherDevicesCallback) {
            return;
        }
        channel.RestoreCallback(GetDmaCallback(kind));
    }

    private DmaChannel.DmaCallback? GetDmaCallback(SoundBlasterDmaCallback kind) {
        return kind switch {
            SoundBlasterDmaCallback.Transfer => DspDmaCallback,
            SoundBlasterDmaCallback.DmaIdentification => DspE2DmaCallback,
            SoundBlasterDmaCallback.Recording => DspAdcCallback,
            _ => null
        };
    }

    private ImmutableArray<SoundBlasterScheduledEvent> CaptureEvents(double now) {
        ImmutableArray<SoundBlasterScheduledEvent>.Builder events = ImmutableArray.CreateBuilder<SoundBlasterScheduledEvent>();
        foreach (SoundBlasterEvent kind in OwnEvents) {
            foreach (QueuedEvent queuedEvent in _scheduler.GetEvents(GetEventHandler(kind))) {
                events.Add(new SoundBlasterScheduledEvent(kind, queuedEvent.TimeMs - now, queuedEvent.Value));
            }
        }
        events.Sort(static (left, right) => left.DelayMs.CompareTo(right.DelayMs));
        return events.ToImmutable();
    }

    private void RestoreEvents(ImmutableArray<SoundBlasterScheduledEvent> events) {
        foreach (SoundBlasterEvent kind in OwnEvents) {
            _scheduler.RemoveEvents(GetEventHandler(kind));
        }
        foreach (SoundBlasterScheduledEvent scheduledEvent in events) {
            _scheduler.AddEvent(GetEventHandler(scheduledEvent.Event), scheduledEvent.DelayMs, scheduledEvent.Value);
        }
    }

    private EventHandler GetEventHandler(SoundBlasterEvent kind) {
        return kind switch {
            SoundBlasterEvent.FinishReset => DspFinishResetEvent,
            SoundBlasterEvent.RaiseIrq => DspRaiseIrqEvent,
            SoundBlasterEvent.ProcessDmaTransfer => ProcessDmaTransferEvent,
            SoundBlasterEvent.SuppressDmaTransfer => SuppressDmaTransfer,
            SoundBlasterEvent.PerTick => _perTickHandler,
            SoundBlasterEvent.PerFrame => _perFrameHandler,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown Sound Blaster event.")
        };
    }
}
