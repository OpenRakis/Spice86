namespace Spice86.Core.Emulator.Devices.Sound.Blaster;

using Serilog.Events;

using Spice86.Audio.Backend;
using Spice86.Audio.Common;
using Spice86.Audio.Filters;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.DirectMemoryAccess;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.VM.Clock;
using Spice86.Core.Emulator.VM.EmulationLoopScheduler;
using Spice86.Shared.Interfaces;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Emulates a Sound Blaster audio device, providing digital audio playback, mixing, and hardware-level DSP command
/// support for various Sound Blaster models.
/// </summary>
/// <remarks>
/// 2022-2025 The DOSBox Staging Team
/// </remarks>
public partial class SoundBlaster : DefaultIOPortHandler, IRequestInterrupt, IBlasterEnvVarProvider, IAudioQueueDevice<AudioFrame>, IMixerQueueNotifier {
    /// <summary>
    /// Initializes a new instance of the Sound Blaster device.
    /// </summary>
    /// <param name="ioPortDispatcher">I/O port dispatcher.</param>
    /// <param name="state">The CPU registers and flags.</param>
    /// <param name="dmaBus">DMA bus for audio transfers.</param>
    /// <param name="dualPic">The dual PIC.</param>
    /// <param name="mixer">The global software mixer.</param>
    /// <param name="opl">OPL synthesizer for FM output.</param>
    /// <param name="loggerService">The logger service.</param>
    /// <param name="scheduler">The event scheduler.</param>
    /// <param name="clock">The emulated clock.</param>
    /// <param name="soundBlasterHardwareConfig">Sound Blaster hardware configuration.</param>
    public SoundBlaster(
        IOPortDispatcher ioPortDispatcher,
        State state,
        DmaBus dmaBus,
        DualPic dualPic,
        SoftwareMixer mixer,
        Opl3Fm opl,
        ILoggerService loggerService,
        EmulationLoopScheduler scheduler,
        IEmulatedClock clock,
        SoundBlasterHardwareConfig soundBlasterHardwareConfig)
        : base(state, false, loggerService) {

        _config = soundBlasterHardwareConfig;
        _dualPic = dualPic;
        _mixer = mixer;
        _opl = opl;
        _scheduler = scheduler;
        _clock = clock;

        // Create queue first with initial capacity. Will be resized in callback.
        const int initialQueueSize = 256;
        _outputQueue = new RWQueue<AudioFrame>(initialQueueSize);

        // Register after queue exists so NotifyLockMixer won't hit a null queue
        mixer.RegisterQueueNotifier(this);

        // Lock mixer thread during construction to prevent concurrent modifications
        mixer.LockMixerThread();

        _primaryDmaChannel = dmaBus.GetChannel(_config.LowDma)
            ?? throw new InvalidOperationException($"DMA channel {_config.LowDma} unavailable for Sound Blaster.");

        _secondaryDmaChannel = ShouldUseHighDmaChannel()
            ? dmaBus.GetChannel(_config.HighDma)
            : null;

        if (_primaryDmaChannel.ChannelNumber == 4 ||
            (_secondaryDmaChannel is not null && _secondaryDmaChannel.ChannelNumber == 4)) {
            throw new InvalidOperationException("Sound Blaster cannot attach to cascade DMA channel 4.");
        }

        _primaryDmaChannel.ReserveFor("SoundBlaster", OnDmaChannelEvicted);

        if (_secondaryDmaChannel is not null) {
            _secondaryDmaChannel.ReserveFor("SoundBlaster16", OnDmaChannelEvicted);
        }

        _sb.Type = _config.SbType;
        _sb.Hw.Base = _config.BaseAddress;
        _sb.Hw.Irq = _config.Irq;
        _sb.Hw.Dma8 = _config.LowDma;
        _sb.Hw.Dma16 = _config.HighDma;

        const int ColdWarmupMs = 100;
        _sb.Dsp.ColdWarmupMs = ColdWarmupMs;
        _sb.Dsp.HotWarmupMs = ColdWarmupMs / 32;
        _sb.FreqHz = DefaultPlaybackRateHz;
        _sb.TimeConstant = 45;

        HashSet<ChannelFeature> dacFeatures = new HashSet<ChannelFeature> {
            ChannelFeature.ReverbSend,
            ChannelFeature.ChorusSend,
            ChannelFeature.DigitalAudio,
            ChannelFeature.Sleep
        };
        if (_config.SbType == SbType.SBPro1 || _config.SbType == SbType.SBPro2 || _config.SbType == SbType.Sb16) {
            dacFeatures.Add(ChannelFeature.Stereo);
        }

        _dacChannel = _mixer.AddChannel(MixerCallback, (int)_sb.FreqHz, "SoundBlasterDAC", dacFeatures);

        // Size to 2x blocksize. The mixer callback typically requests 1x blocksize.
        // This helps prevent queue underruns/stalls under load.
        _outputQueue.Resize((int)Math.Ceiling(_dacChannel.FramesPerBlock * 2.0f));

        if (_config.SbType == SbType.SBPro2) {
            const int NativeDacRateHz = 45454;
            _dacChannel.SetZeroOrderHoldUpsamplerTargetRate(NativeDacRateHz);
            _dacChannel.SetResampleMethod(ResampleMethod.ZeroOrderHoldAndResample);
        }

        _sb.Mixer.Enabled = soundBlasterHardwareConfig.OplConfig.SbMixer;
        _sb.Mixer.StereoEnabled = false;

        DspReset();
        CtmixerReset();

        InitSpeakerState();
        InitPortHandlers(ioPortDispatcher);

        _dualPic.SetIrqMask(_config.Irq, false);

        if (_loggerService.IsEnabled(LogEventLevel.Information)) {
            string highDmaSegment = ShouldUseHighDmaChannel() ? $", high DMA {_config.HighDma}" : string.Empty;
            _loggerService.Information(
                "SoundBlaster: Initialized {SbType} on port {Port:X3}, IRQ {Irq}, DMA {LowDma}{HighDmaSegment}",
                _sb.Type, _sb.Hw.Base, _sb.Hw.Irq, _sb.Hw.Dma8, highDmaSegment);
        }

        mixer.UnlockMixerThread();
    }

    /// <inheritdoc />
    public RWQueue<AudioFrame> OutputQueue => _outputQueue;

    /// <inheritdoc />
    public SoundChannel Channel => _dacChannel;

    public bool PendingIrq8Bit {
        get => _sb.Irq.Pending8Bit;
        set => _sb.Irq.Pending8Bit = value;
    }

    public bool PendingIrq16Bit {
        get => _sb.Irq.Pending16Bit;
        set => _sb.Irq.Pending16Bit = value;
    }

    public bool IsSpeakerEnabled => _sb.SpeakerEnabled;

    public uint DspFrequencyHz => _sb.FreqHz;

    public byte DspTestRegister => _sb.Dsp.TestRegister;

    public SoundChannel DacChannel => _dacChannel;

    public override byte ReadByte(ushort port) {
        switch (port - _config.BaseAddress) {
            case 0x0A:
                // DSP Read Data Port - returns queued output data
                if (_outputData.Count > 0) {
                    return _outputData.Dequeue();
                }
                return 0;

            case 0x0C:
                // DSP Write Buffer Status Port (Write Command/Data)
                // Bit 7: 0 = ready to accept commands, 1 = busy
                // In emulation, we're always ready to accept writes immediately
                return 0x7F;

            case 0x0E:
                bool hasDataOrIrq = _outputData.Count > 0 || _sb.Irq.Pending8Bit;
                if (_sb.Irq.Pending8Bit) {
                    _sb.Irq.Pending8Bit = false;
                    _dualPic.DeactivateIrq(_config.Irq);
                }
                return (byte)(hasDataOrIrq ? 0x80 : 0x00);

            case 0x0F:
                _sb.Irq.Pending16Bit = false;
                return 0xFF;

            case 0x04:
                return _sb.Mixer.Index;

            case 0x05:
                return CtmixerRead();

            case 0x06:
                return 0xFF;

            default:
                return 0xFF;
        }
    }

    public override void WriteByte(ushort port, byte value) {
        switch (port - _config.BaseAddress) {
            case 0x06:
                DspDoReset(value);
                break;

            case 0x0C:
                DspDoWrite(value);
                break;

            case 0x04:
                _sb.Mixer.Index = value;
                break;

            case 0x05:
                CtmixerWrite(value);
                break;

            case 0x07:
                break;

            default:
                if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                    _loggerService.Debug("SoundBlaster: Unhandled port write {Port:X4}", port);
                }
                break;
        }
    }

    /// <inheritdoc />
    public void NotifyLockMixer() {
        _outputQueue.Stop();
    }

    /// <inheritdoc />
    public void NotifyUnlockMixer() {
        _outputQueue.Start();
    }

    private void DspRaiseIrqEvent(uint val) {
        RaiseIrq(SbIrq.Irq8);
    }

    public void RaiseInterruptRequest() {
        RaiseIrq(SbIrq.Irq8);
    }

    /// <summary>
    /// Gets the configured Sound Blaster type.
    /// </summary>
    public SbType SbTypeProperty => _config.SbType;

    /// <summary>
    /// Gets the configured IRQ line.
    /// </summary>
    public byte IRQ => _config.Irq;

    /// <summary>
    /// Gets the configured base I/O address.
    /// </summary>
    public ushort BaseAddress => _config.BaseAddress;

    /// <summary>
    /// Gets the configured 8-bit DMA channel.
    /// </summary>
    public byte LowDma => _config.LowDma;

    /// <summary>
    /// Gets the configured 16-bit DMA channel.
    /// </summary>
    public byte HighDma => _config.HighDma;

    /// <summary>
    /// Gets the BLASTER environment variable string.
    /// </summary>
    public string BlasterString {
        get {
            string highChannelSegment = ShouldUseHighDmaChannel() ? $" H{_config.HighDma}" : string.Empty;
            return $"A{_config.BaseAddress:X3} I{_config.Irq} D{_config.LowDma}{highChannelSegment} T{(int)_config.SbType}";
        }
    }

    /// <summary>
    /// Mixer callback - called by mixer when it needs audio frames.
    /// </summary>
    private void MixerCallback(int framesRequested) {
        int queueSize = _outputQueue.Size;
        int shortage = Math.Max(framesRequested - queueSize, 0);
        System.Threading.Interlocked.Exchange(ref _framesNeeded, shortage);

        int frames_received = 0;
        int remaining = framesRequested;
        while (remaining > 0) {
            int chunk = Math.Min(remaining, _dequeueBatch.Length);
            int dequeued = _outputQueue.BulkDequeue(_dequeueBatch.AsSpan(0, chunk), chunk);
            if (dequeued > 0) {
                _dacChannel.AddAudioFrames(_dequeueBatch.AsSpan(0, dequeued));
                frames_received += dequeued;
                remaining -= dequeued;
            }

            if (dequeued < chunk) {
                break;
            }
        }

        if (frames_received < framesRequested) {
            _dacChannel.AddSilence();
        }
    }

    private bool MaybeWakeUp() {
        _outputQueue.Start();
        return _dacChannel.WakeUp();
    }

    private void SetChannelRateHz(int requestedRateHz) {
        int rateHz = Math.Clamp(requestedRateHz, MinPlaybackRateHz, NativeDacRateHz);
        if (_dacChannel.SampleRate != rateHz) {
            _dacChannel.SampleRate = rateHz;
        }
    }

    private void DspDmaCallback(DmaChannel channel, DmaChannel.DmaEvent dmaEvent) {
        switch (dmaEvent) {
            case DmaChannel.DmaEvent.ReachedTerminalCount:
                break;

            case DmaChannel.DmaEvent.IsMasked:
                if (_sb.Mode == DspMode.Dma) {
                    double currentTime = _clock.ElapsedTimeMs;
                    double elapsedTime = currentTime - _lastDmaCallbackTime;

                    if (elapsedTime > 0 && _sb.Dma.Rate > 0) {
                        uint samplesToGenerate = (uint)(_sb.Dma.Rate * elapsedTime / 1000.0);

                        if (samplesToGenerate > _sb.Dma.Min) {
                            samplesToGenerate = _sb.Dma.Min;
                        }

                        uint minSize = _sb.Dma.Mul >> SbShift;
                        if (minSize == 0) {
                            minSize = 1;
                        }
                        minSize *= 2;

                        if (_sb.Dma.Left > minSize) {
                            if (samplesToGenerate > (_sb.Dma.Left - minSize)) {
                                samplesToGenerate = _sb.Dma.Left - minSize;
                            }

                            if (!_sb.Dma.AutoInit && _sb.Dma.Left <= _sb.Dma.Min) {
                                samplesToGenerate = 0;
                            }

                            if (samplesToGenerate > 0) {
                                PlayDmaTransfer(samplesToGenerate);
                            }
                        }
                    }

                    _sb.Mode = DspMode.DmaMasked;
                }
                break;

            case DmaChannel.DmaEvent.IsUnmasked:
                if (_sb.Mode == DspMode.DmaMasked && _sb.Dma.Mode != DmaMode.None) {
                    DspChangeMode(DspMode.Dma);

                    FlushRemainingDmaTransfer();

                    MaybeWakeUp();

                    if (channel.BaseCount <= MaxSingleFrameBaseCount) {
                        SetCallbackPerFrame();
                    } else {
                        SetCallbackPerTick();
                    }
                }
                break;
        }
    }

    /// <summary>
    /// DMA callback for E2 identification write routine.
    /// </summary>
    private void DspE2DmaCallback(DmaChannel channel, DmaChannel.DmaEvent dmaEvent) {
        if (dmaEvent == DmaChannel.DmaEvent.IsUnmasked) {
            byte val = (byte)(_sb.E2.Value & 0xff);

            channel.RegisterCallback(null);
            Span<byte> buffer = stackalloc byte[1];
            buffer[0] = val;
            channel.Write(1, buffer);
        }
    }

    /// <summary>
    /// DMA callback for ADC (analog-to-digital converter) - fakes input by writing silence.
    /// </summary>
    private void DspAdcCallback(DmaChannel channel, DmaChannel.DmaEvent dmaEvent) {
        if (dmaEvent != DmaChannel.DmaEvent.IsUnmasked) {
            return;
        }

        Span<byte> buffer = stackalloc byte[1];
        buffer[0] = 128;

        while (_sb.Dma.Left > 0) {
            channel.Write(1, buffer);
            _sb.Dma.Left--;
        }

        RaiseIrq(SbIrq.Irq8);
        channel.RegisterCallback(null);
    }

    private void PlayDmaTransfer(uint bytesRequested) {
        uint lowerBound = _sb.Dma.AutoInit ? bytesRequested : _sb.Dma.Min;
        uint bytesToRead = _sb.Dma.Left <= lowerBound ? _sb.Dma.Left : bytesRequested;

        uint bytesRead = 0;
        uint samples = 0;
        ushort frames = 0;

        byte dma16ToSampleDivisor = _sb.Dma.Mode == DmaMode.Pcm16BitAliased ? (byte)2 : (byte)1;

        byte channels = _sb.Dma.Stereo ? (byte)2 : (byte)1;

        _lastDmaCallbackTime = _clock.ElapsedTimeMs;

        switch (_sb.Dma.Mode) {
            case DmaMode.Adpcm2Bit:
                (bytesRead, samples, frames) = DecodeAdpcmDma(bytesToRead, DecodeAdpcm2Bit);
                break;

            case DmaMode.Adpcm3Bit:
                (bytesRead, samples, frames) = DecodeAdpcmDma(bytesToRead, DecodeAdpcm3Bit);
                break;

            case DmaMode.Adpcm4Bit:
                (bytesRead, samples, frames) = DecodeAdpcmDma(bytesToRead, DecodeAdpcm4Bit);
                break;

            case DmaMode.Pcm8Bit:
                if (_sb.Dma.Stereo) {
                    bytesRead = ReadDma8Bit(bytesToRead, _sb.Dma.RemainSize);
                    samples = bytesRead + _sb.Dma.RemainSize;
                    frames = (ushort)(samples / channels);

                    if (frames > 0) {
                        if (_sb.Dma.Sign) {
                            EnqueueFramesStereo(_sb.Dma.Buf8, samples, true);
                        } else {
                            EnqueueFramesStereo(_sb.Dma.Buf8, samples, false);
                        }
                    }
                    if ((samples & 1) != 0) {
                        _sb.Dma.RemainSize = 1;
                        _sb.Dma.Buf8[0] = _sb.Dma.Buf8[samples - 1];
                    } else {
                        _sb.Dma.RemainSize = 0;
                    }
                } else {
                    bytesRead = ReadDma8Bit(bytesToRead);
                    samples = bytesRead;
                    if (_sb.Dma.Sign) {
                        EnqueueFramesMono(_sb.Dma.Buf8, samples, true);
                    } else {
                        EnqueueFramesMono(_sb.Dma.Buf8, samples, false);
                    }
                }
                break;

            case DmaMode.Pcm16BitAliased:
            case DmaMode.Pcm16Bit:
                if (_sb.Dma.Stereo) {
                    bytesRead = ReadDma16Bit(bytesToRead, _sb.Dma.RemainSize);
                    samples = (bytesRead + _sb.Dma.RemainSize) / dma16ToSampleDivisor;
                    frames = (ushort)(samples / channels);

                    if (frames > 0) {
                        if (_sb.Dma.Sign) {
                            EnqueueFramesStereo16(_sb.Dma.Buf16, samples, true);
                        } else {
                            EnqueueFramesStereo16(_sb.Dma.Buf16, samples, false);
                        }
                    }
                    if ((samples & 1) != 0) {
                        _sb.Dma.RemainSize = 1;
                        _sb.Dma.Buf16[0] = _sb.Dma.Buf16[samples - 1];
                    } else {
                        _sb.Dma.RemainSize = 0;
                    }
                } else {
                    bytesRead = ReadDma16Bit(bytesToRead);
                    samples = bytesRead / dma16ToSampleDivisor;

                    if (_sb.Dma.Sign) {
                        EnqueueFramesMono16(_sb.Dma.Buf16, samples, true);
                    } else {
                        EnqueueFramesMono16(_sb.Dma.Buf16, samples, false);
                    }
                }
                break;

            default:
                _loggerService.Warning("SOUNDBLASTER: Unhandled DMA mode {Mode}", _sb.Dma.Mode);
                _sb.Mode = DspMode.None;
                return;
        }

        if (frames > samples) {
            _loggerService.Error("SOUNDBLASTER: Frames {Frames} should never exceed samples {Samples}", frames, samples);
        }

        if (_sb.Dma.FirstTransfer && samples == 1) {
            _sb.Dma.RemainSize = 0;
        }
        _sb.Dma.FirstTransfer = false;

        _sb.Dma.Left -= bytesRead;

        if (_sb.Dma.Left == 0) {

            if (_sb.Dma.Mode >= DmaMode.Pcm16Bit) {
                RaiseIrq(SbIrq.Irq16);
            } else {
                RaiseIrq(SbIrq.Irq8);
            }

            if (!_sb.Dma.AutoInit) {
                if (_sb.Dma.SingleSize == 0) {
                    _sb.Mode = DspMode.None;
                    _sb.Dma.Mode = DmaMode.None;
                } else {
                    _sb.Dma.Left = _sb.Dma.SingleSize;
                    _sb.Dma.SingleSize = 0;
                }
            } else {
                if (_sb.Dma.AutoSize == 0) {
                    _sb.Mode = DspMode.None;
                }
                _sb.Dma.Left = _sb.Dma.AutoSize;
            }
        }
    }

    private (uint bytesRead, uint samples, ushort frames) DecodeAdpcmDma(
        uint bytesToRead,
        Func<byte, byte, ushort, (byte[], byte, ushort)> decodeAdpcmFn) {

        uint numBytes = ReadDma8Bit(bytesToRead);
        uint numSamples = 0;
        ushort numFrames = 0;

        // Parse the reference ADPCM byte, if provided
        uint i = 0;
        if (numBytes > 0 && _sb.Adpcm.HaveRef) {
            _sb.Adpcm.HaveRef = false;
            _sb.Adpcm.Reference = _sb.Dma.Buf8[0];
            _sb.Adpcm.Stepsize = MinAdaptiveStepSize;
            ++i;
        }

        // Decode the remaining DMA buffer into samples using the provided function
        while (i < numBytes) {
            (byte[] decoded, byte reference, ushort stepsize) = decodeAdpcmFn(
                _sb.Dma.Buf8[i], _sb.Adpcm.Reference, _sb.Adpcm.Stepsize);

            _sb.Adpcm.Reference = reference;
            _sb.Adpcm.Stepsize = stepsize;

            byte numDecoded = (byte)decoded.Length;
            EnqueueFramesMono(decoded, numDecoded, false);
            numSamples += numDecoded;
            i++;
        }

        // ADPCM is mono
        numFrames = (ushort)numSamples;
        return (numBytes, numSamples, numFrames);
    }

    internal void EnqueueFramesMono(byte[] samples, uint numSamples, bool signed) {
        if (numSamples == 0) {
            return;
        }

        // Return silent frames if still in warmup
        if (_sb.Dsp.WarmupRemainingMs > 0) {
            _sb.Dsp.WarmupRemainingMs--;
            EnqueueSilentFrames(numSamples);
            return;
        }

        // Return silent frames if speaker is disabled
        if (!_sb.SpeakerEnabled) {
            EnqueueSilentFrames(numSamples);
            return;
        }

        _enqueueBatchCount = 0;
        for (uint i = 0; i < numSamples; i++) {
            float value = signed
                ? LookupTables.ToSigned8(unchecked((sbyte)samples[i]))
                : LookupTables.ToUnsigned8(samples[i]);
            _enqueueBatch[_enqueueBatchCount++] = new AudioFrame(value, value);
        }
        FlushEnqueueBatch();
        _framesAddedThisTick += (int)numSamples;
    }

    /// <summary>
    /// Enqueues silent frames in batch. Used during warmup and when speaker is disabled.
    /// </summary>
    private void EnqueueSilentFrames(uint count) {
        _enqueueBatchCount = 0;
        AudioFrame silence = new AudioFrame(0.0f, 0.0f);
        for (uint i = 0; i < count; i++) {
            _enqueueBatch[_enqueueBatchCount++] = silence;
        }
        FlushEnqueueBatch();
        _framesAddedThisTick += (int)count;
    }

    /// <summary>
    /// Flushes the enqueue batch to the output queue.
    /// Uses RWQueue's NonblockingBulkEnqueue for efficiency.
    /// </summary>
    private void FlushEnqueueBatch() {
        if (_enqueueBatchCount == 0) {
            return;
        }

        _outputQueue.NonblockingBulkEnqueue(_enqueueBatch.AsSpan(0, _enqueueBatchCount), _enqueueBatchCount);
        _enqueueBatchCount = 0;
    }

    internal void EnqueueFramesStereo(byte[] samples, uint numSamples, bool signed) {
        if (numSamples == 0) {
            return;
        }

        uint numFrames = numSamples / 2;

        // Return silent frames if still in warmup
        if (_sb.Dsp.WarmupRemainingMs > 0) {
            _sb.Dsp.WarmupRemainingMs--;
            EnqueueSilentFrames(numFrames);
            return;
        }

        // Return silent frames if speaker is disabled
        if (!_sb.SpeakerEnabled) {
            EnqueueSilentFrames(numFrames);
            return;
        }

        // Batch process samples into AudioFrames
        // Note: SB Pro 1 and 2 swap left/right channels
        bool swapChannels = _sb.Type == SbType.SBPro1 || _sb.Type == SbType.SBPro2;

        _enqueueBatchCount = 0;
        for (uint i = 0; i < numFrames; i++) {
            float left = signed
                ? LookupTables.ToSigned8(unchecked((sbyte)samples[i * 2]))
                : LookupTables.ToUnsigned8(samples[i * 2]);

            float right = signed
                ? LookupTables.ToSigned8(unchecked((sbyte)samples[i * 2 + 1]))
                : LookupTables.ToUnsigned8(samples[i * 2 + 1]);

            if (swapChannels) {
                _enqueueBatch[_enqueueBatchCount++] = new AudioFrame(right, left);
            } else {
                _enqueueBatch[_enqueueBatchCount++] = new AudioFrame(left, right);
            }
        }
        FlushEnqueueBatch();
        _framesAddedThisTick += (int)numFrames;
    }

    internal void EnqueueFramesMono16(short[] samples, uint numSamples, bool signed) {
        if (numSamples == 0) {
            return;
        }

        // Return silent frames if still in warmup
        if (_sb.Dsp.WarmupRemainingMs > 0) {
            _sb.Dsp.WarmupRemainingMs--;
            EnqueueSilentFrames(numSamples);
            return;
        }

        // Return silent frames if speaker is disabled
        if (!_sb.SpeakerEnabled) {
            EnqueueSilentFrames(numSamples);
            return;
        }

        // Batch process samples into AudioFrames
        _enqueueBatchCount = 0;
        for (uint i = 0; i < numSamples; i++) {
            float value = signed
                ? LookupTables.ToSigned16(samples[i])
                : LookupTables.ToUnsigned16((ushort)samples[i]);
            _enqueueBatch[_enqueueBatchCount++] = new AudioFrame(value, value);
        }
        FlushEnqueueBatch();
        _framesAddedThisTick += (int)numSamples;
    }

    internal void EnqueueFramesStereo16(short[] samples, uint numSamples, bool signed) {
        if (numSamples == 0) {
            return;
        }

        uint numFrames = numSamples / 2;

        // Return silent frames if still in warmup
        if (_sb.Dsp.WarmupRemainingMs > 0) {
            _sb.Dsp.WarmupRemainingMs--;
            EnqueueSilentFrames(numFrames);
            return;
        }

        // Return silent frames if speaker is disabled
        if (!_sb.SpeakerEnabled) {
            EnqueueSilentFrames(numFrames);
            return;
        }

        // Batch process samples into AudioFrames
        // Note: SB Pro 1 and 2 swap left/right channels
        bool swapChannels = _sb.Type == SbType.SBPro1 || _sb.Type == SbType.SBPro2;

        _enqueueBatchCount = 0;
        for (uint i = 0; i < numFrames; i++) {
            float left = signed
                ? LookupTables.ToSigned16(samples[i * 2])
                : LookupTables.ToUnsigned16((ushort)samples[i * 2]);

            float right = signed
                ? LookupTables.ToSigned16(samples[i * 2 + 1])
                : LookupTables.ToUnsigned16((ushort)samples[i * 2 + 1]);

            if (swapChannels) {
                _enqueueBatch[_enqueueBatchCount++] = new AudioFrame(right, left);
            } else {
                _enqueueBatch[_enqueueBatchCount++] = new AudioFrame(left, right);
            }
        }
        FlushEnqueueBatch();
        _framesAddedThisTick += (int)numFrames;
    }

    /// <summary>
    /// Raises an IRQ for the Sound Blaster.
    /// </summary>
    private void RaiseIrq(SbIrq irqType) {
        switch (irqType) {
            case SbIrq.Irq8:
                // Don't raise if already pending
                if (_sb.Irq.Pending8Bit) {
                    return;
                }
                _sb.Irq.Pending8Bit = true;
                _dualPic.ActivateIrq(_sb.Hw.Irq);
                if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                    _loggerService.Verbose("SOUNDBLASTER: Raised 8-bit IRQ {Irq}", _sb.Hw.Irq);
                }
                break;

            case SbIrq.Irq16:
                // Don't raise if already pending
                if (_sb.Irq.Pending16Bit) {
                    return;
                }
                _sb.Irq.Pending16Bit = true;
                _dualPic.ActivateIrq(_sb.Hw.Irq);
                if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                    _loggerService.Verbose("SOUNDBLASTER: Raised 16-bit IRQ {Irq}", _sb.Hw.Irq);
                }
                break;

            case SbIrq.IrqMpu:
                // MPU-401 IRQ handling not implemented yet
                _loggerService.Warning("SOUNDBLASTER: MPU-401 IRQ not yet implemented");
                break;
        }
    }

    /// <summary>
    /// Per-tick callback for audio generation.
    /// This callback is run once per emulator tick (every 1ms), so it generates a
    /// batch of frames covering each 1ms time period. For example, if the Sound
    /// Blaster's running at 8 kHz, then that's 8 frames per call. Many rates aren't
    /// evenly divisible by 1000 (For example, 22050 Hz is 22.05 frames/millisecond),
    /// so this function keeps track of exact fractional frames and uses rounding to
    /// ensure partial frames are accounted for and generated across N calls.
    /// </summary>
    private void PerTickCallback(uint _) {
        // assert(sblaster);
        // assert(sblaster->channel);

        // if (!sblaster->channel->is_enabled) {
        //     callback_type.SetNone();
        //     return;
        // }
        if (!_dacChannel.IsEnabled) {
            SetCallbackNone();
            return;
        }

        int frames_needed_val = System.Threading.Interlocked.Exchange(ref _framesNeeded, 0);
        float frames_per_tick = _dacChannel.FramesPerTick;
        _frameCounter += Math.Max(frames_needed_val, frames_per_tick);

        int total_frames = (int)Math.Floor(_frameCounter);
        _frameCounter -= total_frames;

        while (_framesAddedThisTick < total_frames) {
            GenerateFrames(total_frames - _framesAddedThisTick);
        }

        _framesAddedThisTick -= total_frames;
        AddPerTickCallback();
    }

    /// <summary>
    /// Per-frame callback for fine-grained audio generation.
    /// Used for very short DMA transfers where per-tick callbacks would be too infrequent.
    /// </summary>
    private void PerFrameCallback(uint _) {
        if (!_dacChannel.IsEnabled) {
            SetCallbackNone();
            return;
        }

        int mixer_needs = Math.Max(System.Threading.Interlocked.Exchange(ref _framesNeeded, 0), 1);

        _framesAddedThisTick = 0;
        while (_framesAddedThisTick < mixer_needs) {
            GenerateFrames(mixer_needs - _framesAddedThisTick);
        }

        AddNextFrameCallback();
    }

    /// <summary>
    /// Schedules the next per-frame callback.
    /// </summary>
    private void AddNextFrameCallback() {
        double millisPerFrame = _dacChannel.MillisPerFrame;
        _scheduler.AddEvent(PerFrameCallback, millisPerFrame, 0);
    }

    /// <summary>
    /// Stops the current callback type.
    /// </summary>
    private void SetCallbackNone() {
        if (_timingType != TimingType.None) {
            if (_timingType == TimingType.PerTick) {
                _scheduler.RemoveEvents(PerTickCallback);
            } else {
                _scheduler.RemoveEvents(PerFrameCallback);
            }

            _timingType = TimingType.None;
        }
    }

    /// <summary>
    /// Switches to per-tick callback mode (every 1ms).
    /// </summary>
    private void SetCallbackPerTick() {
        if (_timingType != TimingType.PerTick) {
            SetCallbackNone();

            _framesAddedThisTick = 0;
            AddPerTickCallback();

            _timingType = TimingType.PerTick;
        }
    }

    private void AddPerTickCallback() {
        _scheduler.AddEvent(PerTickCallback, 1);
    }

    /// <summary>
    /// Switches to per-frame callback mode (at sample rate frequency).
    /// Used for very short DMA transfers.
    /// </summary>
    private void SetCallbackPerFrame() {
        if (_timingType != TimingType.PerFrame) {
            SetCallbackNone();

            AddNextFrameCallback();

            _timingType = TimingType.PerFrame;
        }
    }

    private void GenerateFrames(int frames_requested) {
        switch (_sb.Mode) {
            case DspMode.None:
            case DspMode.DmaPause:
            case DspMode.DmaMasked: {
                    EnqueueSilentFrames((uint)frames_requested);
                    break;
                }

            case DspMode.Dac:
                // DAC mode typically renders one frame at a time because the
                // DOS program will be writing to the DAC register at the
                // playback rate. In a mixer underflow situation, we render the
                // current frame multiple times.
                _enqueueBatchCount = 0;
                for (int i = 0; i < frames_requested; i++) {
                    _enqueueBatch[_enqueueBatchCount++] = _sb.Dac.RenderFrame();
                    if (_enqueueBatchCount == _enqueueBatch.Length) {
                        FlushEnqueueBatch();
                    }
                }
                FlushEnqueueBatch();
                _framesAddedThisTick += frames_requested;
                break;

            case DspMode.Dma: {
                    MaybeWakeUp();

                    uint len = (uint)frames_requested;
                    len *= _sb.Dma.Mul;
                    if ((len & SbShiftMask) != 0) {
                        len += 1 << SbShift;
                    }
                    len >>= SbShift;

                    if (len > _sb.Dma.Left) {
                        len = _sb.Dma.Left;
                    }

                    PlayDmaTransfer(len);
                    break;
                }
        }
    }

    private void EnqueueFrames(ReadOnlySpan<AudioFrame> frames) {
        if (frames.Length == 0) {
            return;
        }
        _framesAddedThisTick += frames.Length;
        _enqueueBatchCount = 0;
        for (int i = 0; i < frames.Length; i++) {
            _enqueueBatch[_enqueueBatchCount++] = frames[i];
            if (_enqueueBatchCount == _enqueueBatch.Length) {
                FlushEnqueueBatch();
            }
        }
        FlushEnqueueBatch();
    }

    private void DspDoReset(byte value) {
        if (((value & 1) != 0) && (_sb.Dsp.State != DspState.Reset)) {
            // TODO: Get out of highspeed mode
            DspReset();
            _sb.Dsp.State = DspState.Reset;
        } else if (((value & 1) == 0) && (_sb.Dsp.State == DspState.Reset)) {
            _sb.Dsp.State = DspState.ResetWait;

            _scheduler.RemoveEvents(DspFinishResetEvent);
            _scheduler.AddEvent(DspFinishResetEvent, 20.0 / 1000.0, 0);
        }
    }

    /// <summary>
    /// Event callback for delayed DSP reset completion.
    /// </summary>
    private void DspFinishResetEvent(uint val) {
        DspFinishReset();
    }

    private void DspFinishReset() {
        DspFlushData();
        DspAddData(0xaa);
        _sb.Dsp.State = DspState.Normal;
    }

    private void DspReset() {
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("SoundBlaster: DSP Reset");
        }

        _dualPic.DeactivateIrq(_config.Irq);
        DspChangeMode(DspMode.None);
        DspFlushData();

        _sb.Dsp.Cmd = 0;
        _sb.Dsp.CmdLen = 0;
        _sb.Dsp.In.Pos = 0;

        _sb.Dsp.WriteStatusCounter = 0;
        _sb.Dsp.ResetTally++;

        _scheduler.RemoveEvents(DspFinishResetEvent);

        _sb.Dma.Left = 0;
        _sb.Dma.SingleSize = 0;
        _sb.Dma.AutoSize = 0;
        _sb.Dma.Stereo = false;
        _sb.Dma.Sign = false;
        _sb.Dma.AutoInit = false;
        _sb.Dma.FirstTransfer = true;
        _sb.Dma.Mode = DmaMode.None;
        _sb.Dma.RemainSize = 0;

        _sb.Dma.Channel?.ClearRequest();

        _sb.Adpcm.Reference = 0;
        _sb.Adpcm.Stepsize = 0;
        _sb.Adpcm.HaveRef = false;

        _sb.FreqHz = DefaultPlaybackRateHz;
        _sb.TimeConstant = 45;
        _sb.E2.Value = 0xaa;
        _sb.E2.Count = 0;

        _sb.Irq.Pending8Bit = false;
        _sb.Irq.Pending16Bit = false;

        _dacChannel.SampleRate = (DefaultPlaybackRateHz);

        InitSpeakerState();

        _scheduler.RemoveEvents(ProcessDmaTransferEvent);
    }

    private void DspDoWrite(byte value) {
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("SB: DSP write value=0x{Value:X2} state={State}", value, _blasterState);
        }
        switch (_blasterState) {
            case BlasterState.WaitingForCommand:
                _sb.Dsp.Cmd = value;
                if (_config.SbType == SbType.Sb16) {
                    _sb.Dsp.CmdLen = DspCommandLengthsSb16[value];
                } else {
                    _sb.Dsp.CmdLen = DspCommandLengthsSb[value];
                }
                _sb.Dsp.In.Pos = 0;
                _blasterState = BlasterState.ReadingCommand;
                if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                    _loggerService.Debug("SB: Command 0x{Cmd:X2} received; expecting {Len} bytes", _sb.Dsp.Cmd, _sb.Dsp.CmdLen);
                }
                if (_sb.Dsp.CmdLen == 0) {
                    DspDoCommand();
                }
                break;

            case BlasterState.ReadingCommand:
                _sb.Dsp.In.Data[_sb.Dsp.In.Pos] = value;
                _sb.Dsp.In.Pos++;
                if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                    _loggerService.Verbose("SB: Command 0x{Cmd:X2} param[{Count}] = 0x{Val:X2}", _sb.Dsp.Cmd, _sb.Dsp.In.Pos - 1, value);
                }
                if (_sb.Dsp.In.Pos >= _sb.Dsp.CmdLen) {
                    DspDoCommand();
                }
                break;
        }
    }

    private void DspDoCommand() {
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            string paramsHex = _sb.Dsp.In.Pos > 0 ? string.Join(" ", _sb.Dsp.In.Data.Take(_sb.Dsp.In.Pos).Select(b => b.ToString("X2"))) : string.Empty;
            _loggerService.Debug("SB: Processing command 0x{Cmd:X2} params={Params}", _sb.Dsp.Cmd, paramsHex);
        }
        switch (_sb.Dsp.Cmd) {
            case 0x04:
                if (_config.SbType == SbType.Sb16) {
                    if ((_sb.Dsp.In.Data[0] & 0xf1) == 0xf1) {
                        // asp_init_in_progress = true
                    }
                } else {
                    DspFlushData();
                    if (_config.SbType == SbType.SB2) {
                        DspAddData(0x88);
                    } else if (_config.SbType == SbType.SBPro1 || _config.SbType == SbType.SBPro2) {
                        DspAddData(0x7b);
                    } else {
                        DspAddData(0xff);
                    }
                }
                break;

            case 0x05:
                if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                    _loggerService.Debug("DSP Unhandled SB16ASP command 0x{Cmd:X2} (set codec parameter)", _sb.Dsp.Cmd);
                }
                // No specific action needed - ASP commands are mostly unimplemented
                break;

            case 0x08: // SB16 ASP get version
                if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                    _loggerService.Debug("DSP Unhandled SB16ASP command {Cmd:X} sub {Sub:X}",
                                        _sb.Dsp.Cmd,
                                        _sb.Dsp.In.Pos > 0 ? _sb.Dsp.In.Data[0] : 0);
                }

                if (_config.SbType == SbType.Sb16 && _sb.Dsp.In.Pos >= 1) {
                    switch (_sb.Dsp.In.Data[0]) {
                        case 0x03:
                            DspAddData(0x18); // version ID (??)
                            break;

                        default:
                            if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                                _loggerService.Debug("DSP Unhandled SB16ASP command {Cmd:X} sub {Sub:X}",
                                                    _sb.Dsp.Cmd,
                                                    _sb.Dsp.In.Data[0]);
                            }
                            break;
                    }
                }
                break;

            case 0x0e:
                if (_config.SbType == SbType.Sb16) {
                    // asp_regs[_sb.Dsp.In.Data[0]] = _sb.Dsp.In.Data[1]
                }
                break;

            case 0x0f:
                if (_config.SbType == SbType.Sb16) {
                    DspAddData(0x00);
                }
                break;

            case 0x10:
                DspChangeMode(DspMode.Dac);

                MaybeWakeUp();

                SetCallbackPerFrame();
                break;

            case 0x14:
            case 0x15:
            case 0x91:
                DspPrepareDmaOld(DmaMode.Pcm8Bit, false, false);
                break;

            case 0x1c:
            case 0x90:
                if (_config.SbType > SbType.SB1) {
                    DspPrepareDmaOld(DmaMode.Pcm8Bit, true, false);
                } else {
                    _loggerService.Warning("SOUNDBLASTER: Auto-init DMA not supported on {SbType}", _config.SbType);
                }
                break;

            case 0x7f:
            case 0x1f:
                if (_config.SbType > SbType.SB1) {
                    if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                        _loggerService.Error("DSP:Unimplemented auto-init DMA ADPCM command {Cmd:X2}",
                                            _sb.Dsp.Cmd);
                    }
                }
                break;

            case 0x20:
                DspAddData(0x7f);
                break;

            case 0x24:
                _sb.Dma.Left = (uint)(1 + _sb.Dsp.In.Data[0] + (_sb.Dsp.In.Data[1] << 8));
                _sb.Dma.Sign = false;
                if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                    _loggerService.Debug("SB: Faked ADC for {Size} bytes", _sb.Dma.Left);
                }
                _primaryDmaChannel.RegisterCallback(DspAdcCallback);
                break;

            case 0x30:
            case 0x31:
                if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                    _loggerService.Error("DSP:Unimplemented MIDI I/O command {Cmd:X2}",
                                        _sb.Dsp.Cmd);
                }
                break;

            case 0x34:
            case 0x35:
            case 0x36:
            case 0x37:
                if (_config.SbType > SbType.SB1) {
                    if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                        _loggerService.Error("DSP:Unimplemented MIDI UART command {Cmd:X2}",
                                            _sb.Dsp.Cmd);
                    }
                }
                break;

            case 0x38:
                if (_sb.MidiEnabled) {
                    if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                        _loggerService.Verbose("SB: MIDI output byte 0x{Byte:X2}", _sb.Dsp.In.Data[0]);
                    }
                }
                break;

            case 0x40:
                DspChangeRate((uint)(1000000 / (256 - _sb.Dsp.In.Data[0])));

                if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                    _loggerService.Debug("SB: Timeconstant set tc=0x{Tc:X2} rate={Rate}Hz", _sb.Dsp.In.Data[0], _sb.FreqHz);
                }
                break;

            case 0x41:
            case 0x42:
                if (_config.SbType == SbType.Sb16) {
                    DspChangeRate((uint)((_sb.Dsp.In.Data[0] << 8) | _sb.Dsp.In.Data[1]));
                }
                break;

            case 0x48:
                if (_config.SbType > SbType.SB1) {
                    _sb.Dma.AutoSize = (uint)(1 + _sb.Dsp.In.Data[0] + (_sb.Dsp.In.Data[1] << 8));
                    if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                        _loggerService.Debug("SB: DMA AutoSize set to {AutoSize}", _sb.Dma.AutoSize);
                    }
                }
                break;

            case 0x16:
            case 0x17:
                if (_sb.Dsp.Cmd == 0x17) {
                    _sb.Adpcm.HaveRef = true;
                }
                _sb.Dma.Left = (uint)(1 + _sb.Dsp.In.Data[0] + (_sb.Dsp.In.Data[1] << 8));
                if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                    _loggerService.Debug("SB: 2-bit ADPCM size={Size} haveRef={HaveRef}", _sb.Dma.Left, _sb.Adpcm.HaveRef);
                }
                DspPrepareDmaOld(DmaMode.Adpcm2Bit, false, false);
                break;

            case 0x74:
            case 0x75:
                if (_sb.Dsp.Cmd == 0x75) {
                    _sb.Adpcm.HaveRef = true;
                }
                DspPrepareDmaOld(DmaMode.Adpcm4Bit, false, false);
                break;

            case 0x76:
            case 0x77:
                if (_sb.Dsp.Cmd == 0x77) {
                    _sb.Adpcm.HaveRef = true;
                }
                DspPrepareDmaOld(DmaMode.Adpcm3Bit, false, false);
                break;

            case 0x7d:
                if (_config.SbType > SbType.SB1) {
                    _sb.Adpcm.HaveRef = true;
                    DspPrepareDmaOld(DmaMode.Adpcm4Bit, true, false);
                }
                break;

            case 0x80: {
                    uint samples = (uint)(1 + _sb.Dsp.In.Data[0] + (_sb.Dsp.In.Data[1] << 8));
                    double delayMs = (1000.0 * samples) / _sb.FreqHz;
                    _scheduler.AddEvent(DspRaiseIrqEvent, delayMs, 0);
                }
                break;

            case 0x98:
            case 0x99:
            case 0xa0:
            case 0xa8: // Documented only for DSP 3.x
                if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                    _loggerService.Error("DSP:Unimplemented input command {Cmd:X2}",
                                        _sb.Dsp.Cmd);
                }
                break;

            // Generic 8/16-bit DMA commands (SB16 only) - 0xB0-0xCF
            // Reference: DOSBox soundblaster.cpp lines 2068-2097
            case 0xb0:
            case 0xb1:
            case 0xb2:
            case 0xb3:
            case 0xb4:
            case 0xb5:
            case 0xb6:
            case 0xb7:
            case 0xb8:
            case 0xb9:
            case 0xba:
            case 0xbb:
            case 0xbc:
            case 0xbd:
            case 0xbe:
            case 0xbf:
            case 0xc0:
            case 0xc1:
            case 0xc2:
            case 0xc3:
            case 0xc4:
            case 0xc5:
            case 0xc6:
            case 0xc7:
            case 0xc8:
            case 0xc9:
            case 0xca:
            case 0xcb:
            case 0xcc:
            case 0xcd:
            case 0xce:
            case 0xcf:
                // Generic DMA commands (SB16 only)
                // Reference: src/hardware/audio/soundblaster.cpp case 0xb0-0xcf
                if (_config.SbType == SbType.Sb16) {
                    // Parse command byte and mode byte
                    // Command bit 4 (0x10): 0=8-bit, 1=16-bit
                    // Mode byte bit 4 (0x10): signed data
                    // Mode byte bit 5 (0x20): stereo
                    // Command bit 2 (0x04): FIFO enable (we don't emulate FIFO delay)

                    _sb.Dma.Sign = (_sb.Dsp.In.Data[0] & 0x10) != 0;
                    bool is16Bit = (_sb.Dsp.Cmd & 0x10) != 0;
                    bool autoInit = (_sb.Dsp.Cmd & 0x04) != 0;
                    bool stereo = (_sb.Dsp.In.Data[0] & 0x20) != 0;

                    // Length is in bytes (for 8-bit) or words (for 16-bit)
                    uint length = (uint)(1 + _sb.Dsp.In.Data[1] + (_sb.Dsp.In.Data[2] << 8));

                    if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                        _loggerService.Debug("SB16: Generic DMA cmd=0x{Cmd:X2} mode=0x{Mode:X2} " +
                            "16bit={Is16Bit} autoInit={AutoInit} stereo={Stereo} sign={Sign} len={Length}",
                            _sb.Dsp.Cmd, _sb.Dsp.In.Data[0], is16Bit, autoInit, stereo, _sb.Dma.Sign, length);
                    }

                    DspPrepareDmaNew(is16Bit ? DmaMode.Pcm16Bit : DmaMode.Pcm8Bit, length, autoInit, stereo);
                } else {
                    _loggerService.Warning("SOUNDBLASTER: Generic DMA commands (0xB0-0xCF) require SB16");
                }
                break;

            case 0xd0:
                // Halt 8-bit DMA
                _sb.Mode = DspMode.DmaPause;
                break;

            case 0xd1:
                // Enable Speaker
                SetSpeakerEnabled(true);
                break;

            case 0xd3:
                // Disable Speaker
                SetSpeakerEnabled(false);
                break;

            case 0xd4:
                // Continue DMA 8-bit
                if (_sb.Mode == DspMode.DmaPause) {
                    _sb.Mode = DspMode.DmaMasked;
                }
                break;

            case 0xd5:
                // Halt 16-bit DMA
                if (_config.SbType == SbType.Sb16) {
                    _sb.Mode = DspMode.DmaPause;
                }
                break;

            case 0xd6:
                // Continue DMA 16-bit
                if (_config.SbType == SbType.Sb16) {
                    if (_sb.Mode == DspMode.DmaPause) {
                        _sb.Mode = DspMode.DmaMasked;
                    }
                }
                break;

            case 0xd8:
                // Speaker status
                if (_config.SbType > SbType.SB1) {
                    DspFlushData();
                    if (_sb.SpeakerEnabled) {
                        DspAddData(0xff);
                        _sb.Dsp.WarmupRemainingMs = 0;
                    } else {
                        DspAddData(0x00);
                    }
                }
                break;

            case 0xd9:
                // Exit Autoinitialize 16-bit
                if (_config.SbType == SbType.Sb16) {
                    _sb.Dma.AutoInit = false;
                }
                break;

            case 0xda:
                // Exit Autoinitialize 8-bit
                if (_config.SbType > SbType.SB1) {
                    _sb.Dma.AutoInit = false;
                }
                break;

            case 0xe0:
                // DSP Identification
                // Reference: src/hardware/audio/soundblaster.cpp case 0xe0
                DspFlushData();
                DspAddData((byte)~_sb.Dsp.In.Data[0]);
                break;

            case 0xe1:
                // Get DSP Version
                DspFlushData();
                switch (_config.SbType) {
                    case SbType.SB1:
                        DspAddData(0x01);
                        DspAddData(0x05);
                        break;
                    case SbType.SB2:
                        DspAddData(0x02);
                        DspAddData(0x01);
                        break;
                    case SbType.SBPro1:
                        DspAddData(0x03);
                        DspAddData(0x00);
                        break;
                    case SbType.SBPro2:
                        if (_sb.EssType != EssType.None) {
                            DspAddData(0x03);
                            DspAddData(0x01);
                        } else {
                            DspAddData(0x03);
                            DspAddData(0x02);
                        }
                        break;
                    case SbType.Sb16:
                        DspAddData(0x04);
                        DspAddData(0x05);
                        break;
                }
                break;

            case 0xe2:
                for (int i = 0; i < 8; i++) {
                    if (((_sb.Dsp.In.Data[0] >> i) & 0x01) != 0) {
                        _sb.E2.Value += E2IncrTable[_sb.E2.Count % 4][i];
                    }
                }
                _sb.E2.Value += E2IncrTable[_sb.E2.Count % 4][8];
                _sb.E2.Count++;
                _primaryDmaChannel.RegisterCallback(DspE2DmaCallback);
                break;

            case 0xe3:
                // DSP Copyright
                DspFlushData();
                if (_sb.EssType != EssType.None) {
                    DspAddData(0);
                } else {
                    string copyright = "COPYRIGHT (C) CREATIVE TECHNOLOGY LTD, 1992.";
                    foreach (char c in copyright) {
                        DspAddData((byte)c);
                    }
                }
                break;

            case 0xe4:
                _sb.Dsp.TestRegister = _sb.Dsp.In.Data[0];
                break;

            case 0xe7: // ESS detect/read config
                switch (_sb.EssType) {
                    case EssType.None:
                        break;

                    case EssType.Es1688:
                        DspFlushData();
                        // Determined via Windows driver debugging.
                        DspAddData(0x68);
                        DspAddData(0x80 | 0x09);
                        break;
                }
                break;

            case 0xe8:
                // Read Test Register
                DspFlushData();
                DspAddData(_sb.Dsp.TestRegister);
                break;

            case 0xf2:
                _scheduler.AddEvent(DspRaiseIrqEvent, 0.01, 0);
                break;

            case 0xf3:
                if (_config.SbType == SbType.Sb16) {
                    RaiseIrq(SbIrq.Irq16);
                }
                break;

            case 0xf8:
                DspFlushData();
                DspAddData(0);
                break;

            case 0xf9:
                if (_config.SbType == SbType.Sb16) {
                    switch (_sb.Dsp.In.Data[0]) {
                        case 0x0b: DspAddData(0x00); break;
                        case 0x0e: DspAddData(0xff); break;
                        case 0x0f: DspAddData(0x07); break;
                        case 0x23: DspAddData(0x00); break;
                        case 0x24: DspAddData(0x00); break;
                        case 0x2b: DspAddData(0x00); break;
                        case 0x2c: DspAddData(0x00); break;
                        case 0x2d: DspAddData(0x00); break;
                        case 0x37: DspAddData(0x38); break;
                        default: DspAddData(0x00); break;
                    }
                }
                break;

            default:
                if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                    _loggerService.Warning("SoundBlaster: Unimplemented DSP command {Command:X2}", _sb.Dsp.Cmd);
                }
                _sb.Dsp.Cmd = 0;
                _sb.Dsp.CmdLen = 0;
                _sb.Dsp.In.Pos = 0;
                _blasterState = BlasterState.WaitingForCommand;
                return;
        }

        _sb.Dsp.Cmd = 0;
        _sb.Dsp.CmdLen = 0;
        _sb.Dsp.In.Pos = 0;
        _blasterState = BlasterState.WaitingForCommand;
    }

    private void DspChangeMode(DspMode mode) {
        if (_sb.Mode == mode) {
            return;
        }
        _sb.Mode = mode;
    }

    private void DspChangeStereo(bool stereo) {
        if (!_sb.Dma.Stereo && stereo) {
            SetChannelRateHz((int)(_sb.FreqHz / 2));
            _sb.Dma.Mul *= 2;
            _sb.Dma.Rate = (_sb.FreqHz * _sb.Dma.Mul) >> SbShift;
            _sb.Dma.Min = (_sb.Dma.Rate * 3) / 1000;
        } else if (_sb.Dma.Stereo && !stereo) {
            SetChannelRateHz((int)_sb.FreqHz);
            _sb.Dma.Mul /= 2;
            _sb.Dma.Rate = (_sb.FreqHz * _sb.Dma.Mul) >> SbShift;
            _sb.Dma.Min = (_sb.Dma.Rate * 3) / 1000;
        }
        _sb.Dma.Stereo = stereo;
    }

    /// <summary>
    /// Updates the sample rate during playback.
    /// Reference: src/hardware/audio/soundblaster.cpp dsp_change_rate()
    /// </summary>
    private void DspChangeRate(uint freqHz) {
        // If rate changes during active DMA, update the DMA-related timing values
        if (_sb.FreqHz != freqHz && _sb.Dma.Mode != DmaMode.None) {
            uint effectiveFreq = _sb.Mixer.StereoEnabled ? freqHz / 2 : freqHz;
            _dacChannel.SampleRate = ((int)effectiveFreq);

            _sb.Dma.Rate = (freqHz * _sb.Dma.Mul) >> SbShift;
            _sb.Dma.Min = (_sb.Dma.Rate * 3) / 1000;
        }
        _sb.FreqHz = freqHz;
    }

    /// <summary>
    /// Flushes any remaining DMA transfer that's shorter than the minimum threshold.
    /// This handles edge cases where the DMA transfer is so short it wouldn't be processed
    /// by the normal per-tick callback before the next one fires.
    /// Reference: src/hardware/audio/soundblaster.cpp flush_remaining_dma_transfer()
    /// </summary>
    private void FlushRemainingDmaTransfer() {
        if (_sb.Dma.Left == 0) {
            return;
        }

        if (!_sb.SpeakerEnabled && _config.SbType != SbType.Sb16) {
            uint numBytes = Math.Min(_sb.Dma.Min, _sb.Dma.Left);
            double delayMs = (numBytes * 1000.0) / _sb.Dma.Rate;

            _scheduler.AddEvent(SuppressDmaTransfer, delayMs, numBytes);

            if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                _loggerService.Debug("SOUNDBLASTER: Silent DMA Transfer scheduling IRQ in {Delay:F3} milliseconds", delayMs);
            }
        } else if (_sb.Dma.Left < _sb.Dma.Min) {
            double delayMs = (_sb.Dma.Left * 1000.0) / _sb.Dma.Rate;

            if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                _loggerService.Debug("SOUNDBLASTER: Short transfer scheduling IRQ in {Delay:F3} milliseconds", delayMs);
            }

            _scheduler.AddEvent(ProcessDmaTransferEvent, delayMs, _sb.Dma.Left);
        }
    }

    /// <summary>
    /// Event callback to process DMA transfer for flush_remaining_dma_transfer.
    /// Reference: src/hardware/audio/soundblaster.cpp ProcessDMATransfer()
    /// </summary>
    private void ProcessDmaTransferEvent(uint bytesToProcess) {
        if (_sb.Dma.Left > 0) {
            uint toProcess = Math.Min(bytesToProcess, _sb.Dma.Left);
            PlayDmaTransfer(toProcess);
        }
    }

    /// <summary>
    /// Suppresses DMA transfer silently (reads and discards data, raises IRQs).
    /// Used when speaker output is disabled.
    /// Reference: src/hardware/audio/soundblaster.cpp suppress_dma_transfer()
    /// </summary>
    private void SuppressDmaTransfer(uint bytesToRead) {
        uint numBytes = Math.Min(bytesToRead, _sb.Dma.Left);

        // Read and discard the DMA data silently
        DmaChannel? dmaChannel = _sb.Dma.Channel;
        if (dmaChannel is not null && numBytes > 0) {
            Span<byte> discardBuffer = stackalloc byte[(int)Math.Min(numBytes, 4096)];
            uint remaining = numBytes;
            while (remaining > 0) {
                int toRead = (int)Math.Min(remaining, (uint)discardBuffer.Length);
                int read = dmaChannel.Read(toRead, discardBuffer[..toRead]);
                if (read == 0) {
                    break;
                }
                remaining -= (uint)read;
            }
        }

        _sb.Dma.Left -= numBytes;

        if (_sb.Dma.Left == 0) {
            // Raise appropriate IRQ
            if (_sb.Dma.Mode >= DmaMode.Pcm16Bit) {
                RaiseIrq(SbIrq.Irq16);
            } else {
                RaiseIrq(SbIrq.Irq8);
            }

            // Handle auto-init vs single-cycle
            if (_sb.Dma.AutoInit) {
                _sb.Dma.Left = _sb.Dma.AutoSize;
            } else {
                _sb.Mode = DspMode.None;
                _sb.Dma.Mode = DmaMode.None;
            }
        }

        // If more data remains, schedule another suppress
        if (_sb.Dma.Left > 0) {
            uint nextBytes = Math.Min(_sb.Dma.Min, _sb.Dma.Left);
            double delayMs = (nextBytes * 1000.0) / _sb.Dma.Rate;
            _scheduler.AddEvent(SuppressDmaTransfer, delayMs, nextBytes);
        }
    }

    /// <summary>
    /// Core DMA transfer setup - matches DOSBox's dsp_do_dma_transfer().
    /// Reference: src/hardware/audio/soundblaster.cpp dsp_do_dma_transfer()
    /// </summary>
    private void DspDoDmaTransfer(DmaMode mode, uint freqHz, bool autoInit, bool stereo) {
        // Starting a new transfer will clear any active irqs
        // Reference: src/hardware/audio/soundblaster.cpp dsp_do_dma_transfer() lines 1571-1573
        _sb.Irq.Pending8Bit = false;
        _sb.Irq.Pending16Bit = false;
        _dualPic.DeactivateIrq(_config.Irq);

        // Set up the multiplier based on DMA mode
        // Reference: src/hardware/audio/soundblaster.cpp dsp_do_dma_transfer() lines 1575-1588
        _sb.Dma.Mul = mode switch {
            DmaMode.Adpcm2Bit => (1 << SbShift) / 4,
            DmaMode.Adpcm3Bit => (1 << SbShift) / 3,
            DmaMode.Adpcm4Bit => (1 << SbShift) / 2,
            DmaMode.Pcm8Bit => 1 << SbShift,
            DmaMode.Pcm16Bit => 1 << SbShift,
            DmaMode.Pcm16BitAliased => (1 << SbShift) * 2,
            _ => 1 << SbShift
        };

        // Going from an active autoinit into a single cycle
        // Reference: src/hardware/audio/soundblaster.cpp dsp_do_dma_transfer() lines 1590-1601
        if (_sb.Mode >= DspMode.Dma && _sb.Dma.AutoInit && !autoInit) {
            // Don't do anything, the total will flip over on the next transfer
        } else if (!autoInit) {
            // Just a normal single cycle transfer
            _sb.Dma.Left = _sb.Dma.SingleSize;
            _sb.Dma.SingleSize = 0;
        } else {
            // Going into an autoinit transfer - transfer full cycle again
            _sb.Dma.Left = _sb.Dma.AutoSize;
        }

        _sb.Dma.AutoInit = autoInit;
        _sb.Dma.Mode = mode;
        _sb.Dma.Stereo = stereo;

        if (stereo) {
            _sb.Dma.Mul *= 2;
        }

        _sb.Dma.Rate = (freqHz * _sb.Dma.Mul) >> SbShift;
        _sb.Dma.Min = (_sb.Dma.Rate * 3) / 1000;

        _dacChannel.SampleRate = ((int)freqHz);

        _scheduler.RemoveEvents(ProcessDmaTransferEvent);

        _sb.Mode = DspMode.DmaMasked;
        _sb.Dma.Channel?.RegisterCallback(DspDmaCallback);

        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("SOUNDBLASTER: DMA Transfer - Mode={Mode}, Stereo={Stereo}, AutoInit={AutoInit}, FreqHz={FreqHz}, Rate={Rate}, Left={Left}",
                mode, stereo, autoInit, freqHz, _sb.Dma.Rate, _sb.Dma.Left);
        }
    }

    /// <summary>
    /// Prepare DMA transfer for old-style (SB 1.x/2.x) commands.
    /// </summary>
    private void DspPrepareDmaOld(DmaMode mode, bool autoInit, bool sign) {
        _sb.Dma.Sign = sign;

        if (!autoInit) {
            _sb.Dma.SingleSize = (uint)(1 + _sb.Dsp.In.Data[0] + (_sb.Dsp.In.Data[1] << 8));
        }

        _sb.Dma.Channel = _primaryDmaChannel;

        uint freqHz = _sb.FreqHz;
        if (_sb.Mixer.StereoEnabled) {
            freqHz /= 2;
        }

        DspDoDmaTransfer(mode, freqHz, autoInit, _sb.Mixer.StereoEnabled);
    }

    /// <summary>
    /// Prepare DMA transfer for new-style (SB16) commands.
    /// </summary>
    private void DspPrepareDmaNew(DmaMode mode, uint length, bool autoInit, bool stereo) {
        uint freqHz = _sb.FreqHz;
        DmaMode newMode = mode;
        uint newLength = length;

        if (mode == DmaMode.Pcm16Bit) {
            if (_secondaryDmaChannel is not null) {
                _sb.Dma.Channel = _secondaryDmaChannel;
            } else {
                _sb.Dma.Channel = _primaryDmaChannel;
                newMode = DmaMode.Pcm16BitAliased;
                newLength *= 2;
            }
        } else {
            _sb.Dma.Channel = _primaryDmaChannel;
        }

        if (autoInit) {
            _sb.Dma.AutoSize = newLength;
        } else {
            _sb.Dma.SingleSize = newLength;
        }

        DspDoDmaTransfer(newMode, freqHz, autoInit, stereo);
    }

    private void SetSpeakerEnabled(bool enabled) {
        if (_config.SbType == SbType.Sb16 || _sb.EssType != EssType.None) {
            return;
        }
        if (_sb.SpeakerEnabled == enabled) {
            return;
        }

        if (enabled) {
            _scheduler.RemoveEvents(SuppressDmaTransfer);
            FlushRemainingDmaTransfer();

            _sb.Dsp.WarmupRemainingMs = _sb.Dsp.ColdWarmupMs;
        }

        _sb.SpeakerEnabled = enabled;
    }

    private bool ShouldUseHighDmaChannel() {
        return _config.SbType == SbType.Sb16 &&
               _config.HighDma >= 5 &&
               _config.HighDma != _config.LowDma;
    }

    private void InitSpeakerState() {
        if (_config.SbType == SbType.Sb16 || _sb.EssType != EssType.None) {
            bool isColdStart = _sb.Dsp.ResetTally <= DspInitialResetLimit;
            _sb.Dsp.WarmupRemainingMs = isColdStart ? _sb.Dsp.ColdWarmupMs : _sb.Dsp.HotWarmupMs;
            _sb.SpeakerEnabled = true;
        } else {
            _sb.SpeakerEnabled = false;
        }
    }

    private void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
        if (_config.SbType == SbType.None || _config.BaseAddress == 0) {
            return;
        }

        int basePort = _config.BaseAddress;
        ioPortDispatcher.AddIOPortHandler((ushort)(basePort + 0x06), this);
        ioPortDispatcher.AddIOPortHandler((ushort)(basePort + 0x0A), this);
        ioPortDispatcher.AddIOPortHandler((ushort)(basePort + 0x0C), this);
        ioPortDispatcher.AddIOPortHandler((ushort)(basePort + 0x0E), this);
        ioPortDispatcher.AddIOPortHandler((ushort)(basePort + 0x04), this);
        ioPortDispatcher.AddIOPortHandler((ushort)(basePort + 0x05), this);
        if (_sb.Type == SbType.Sb16) {
            ioPortDispatcher.AddIOPortHandler((ushort)(basePort + 0x0F), this);
        }
    }

    private void OnDmaChannelEvicted() {
        if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
            _loggerService.Warning("SOUNDBLASTER: DMA channel evicted - stopping audio");
        }

        // Stop any active DMA transfer
        _sb.Mode = DspMode.None;
        _sb.Dma.Mode = DmaMode.None;
        _sb.Dma.Left = 0;
        _sb.Dma.Channel = null;

        // Clear pending IRQs
        _sb.Irq.Pending8Bit = false;
        _sb.Irq.Pending16Bit = false;
        _dualPic.DeactivateIrq(_config.Irq);
    }

    private void DspFlushData() {
        _outputData.Clear();
    }

    private void DspAddData(byte value) {
        _outputData.Enqueue(value);
    }

    private float CalcVol(byte amount) {
        int count = 31 - amount;
        float db = count;

        if (_sb.Type is SbType.SBPro1 or SbType.SBPro2) {
            if (count != 0) {
                if (count < 16) {
                    db -= 1.0f;
                } else if (count > 16) {
                    db += 1.0f;
                }
                if (count == 24) {
                    db += 2.0f;
                }
                if (count > 27) {
                    return 0.0f;
                }
            }
        } else {
            db *= 2.0f;
            if (count > 20) {
                db -= 1.0f;
            }
        }

        return MathF.Pow(10.0f, -0.05f * db);
    }

    private void CtmixerUpdateVolumes() {
        if (!_sb.Mixer.Enabled) {
            return;
        }

        float m0 = CalcVol(_sb.Mixer.Master[0]);
        float m1 = CalcVol(_sb.Mixer.Master[1]);

        AudioFrame dacVolume = new AudioFrame(m0 * CalcVol(_sb.Mixer.Dac[0]), m1 * CalcVol(_sb.Mixer.Dac[1]));
        _dacChannel.AppVolume = dacVolume;

        SoundChannel oplChannel = _opl.MixerChannel;
        AudioFrame oplVolume = new AudioFrame(m0 * CalcVol(_sb.Mixer.Fm[0]), m1 * CalcVol(_sb.Mixer.Fm[1]));
        oplChannel.AppVolume = oplVolume;

        SoundChannel? cdAudioChannel = _mixer.FindChannel("CdAudio");
        if (cdAudioChannel != null) {
            AudioFrame cdVolume = new AudioFrame(m0 * CalcVol(_sb.Mixer.Cda[0]), m1 * CalcVol(_sb.Mixer.Cda[1]));
            cdAudioChannel.AppVolume = cdVolume;
        }
    }

    private void CtmixerReset() {
        const byte DefaultVolume = 31;

        _sb.Mixer.Fm[0] = DefaultVolume;
        _sb.Mixer.Fm[1] = DefaultVolume;

        _sb.Mixer.Cda[0] = DefaultVolume;
        _sb.Mixer.Cda[1] = DefaultVolume;

        _sb.Mixer.Dac[0] = DefaultVolume;
        _sb.Mixer.Dac[1] = DefaultVolume;

        _sb.Mixer.Master[0] = DefaultVolume;
        _sb.Mixer.Master[1] = DefaultVolume;

        CtmixerUpdateVolumes();
    }

    private void WriteSbProVolume(byte[] dest, byte value) {
        dest[0] = (byte)(((value & 0xF0) >> 3) | (_sb.Type == SbType.Sb16 ? 1 : 3));
        dest[1] = (byte)(((value & 0x0F) << 1) | (_sb.Type == SbType.Sb16 ? 1 : 3));
    }

    private byte ReadSbProVolume(byte[] src) {
        int result = ((src[0] & 0x1E) << 3) | ((src[1] & 0x1E) >> 1);
        if (_sb.Type is SbType.SBPro1 or SbType.SBPro2) {
            result |= 0x11;
        }
        return (byte)result;
    }

    private void WriteEssVolume(byte value, byte[] output) {
        byte high = (byte)((value >> 4) & 0x0F);
        byte low = (byte)(value & 0x0F);

        output[0] = (byte)((high << 1) | (high >> 3));
        output[1] = (byte)((low << 1) | (low >> 3));
    }

    private byte ReadEssVolume(byte[] input) {
        byte high = (byte)(input[0] >> 1);
        byte low = (byte)(input[1] >> 1);
        return (byte)((high << 4) + low);
    }

    private void CtmixerWrite(byte value) {
        switch (_sb.Mixer.Index) {
            case (byte)MixerRegister.Reset:
                CtmixerReset();
                break;

            case (byte)MixerRegister.MasterVolumeSb2:
                WriteSbProVolume(_sb.Mixer.Master, (byte)((value & 0x0F) | (value << 4)));
                CtmixerUpdateVolumes();
                break;

            case (byte)MixerRegister.DacVolumeSbPro:
                WriteSbProVolume(_sb.Mixer.Dac, value);
                CtmixerUpdateVolumes();
                break;

            case (byte)MixerRegister.FmOutputSelection: {
                    WriteSbProVolume(_sb.Mixer.Fm, (byte)((value & 0x0F) | (value << 4)));
                    CtmixerUpdateVolumes();

                    if ((value & 0x60) != 0) {
                        if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                            _loggerService.Warning("Turned FM one channel off. not implemented {Value:X2}", value);
                        }
                    }
                }
                break;

            case (byte)MixerRegister.CdAudioVolumeSb2:
                WriteSbProVolume(_sb.Mixer.Cda, (byte)((value & 0x0F) | (value << 4)));
                CtmixerUpdateVolumes();
                break;

            case (byte)MixerRegister.MicLevelOrDacVolume:
                if (_sb.Type == SbType.SB2) {
                    byte dacValue = (byte)(((value & 0x06) << 2) | 3);
                    _sb.Mixer.Dac[0] = dacValue;
                    _sb.Mixer.Dac[1] = dacValue;
                    CtmixerUpdateVolumes();
                } else {
                    _sb.Mixer.Mic = (byte)(((value & 0x07) << 2) | (_sb.Type == SbType.Sb16 ? 1 : 3));
                }
                break;

            case (byte)MixerRegister.OutputStereoSelect: {
                    _sb.Mixer.StereoEnabled = (value & 0x02) != 0;

                    if (_sb.Type == SbType.SBPro2) {
                        bool lastFilterEnabled = _sb.Mixer.FilterEnabled;
                        _sb.Mixer.FilterEnabled = (value & 0x20) == 0;

                        if (_sb.Mixer.FilterConfigured && _sb.Mixer.FilterEnabled != lastFilterEnabled) {
                            if (_sb.Mixer.FilterAlwaysOn) {
                                if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                                    _loggerService.Debug("{Action} low-pass filter",
                                        _sb.Mixer.FilterEnabled ? "Enabling" : "Disabling");
                                }
                            }
                        }
                    }
                    DspChangeStereo(_sb.Mixer.StereoEnabled);
                    if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                        _loggerService.Warning("Mixer set to {Mode}", _sb.Dma.Stereo ? "STEREO" : "MONO");
                    }
                }
                break;

            case (byte)MixerRegister.Audio1PlayVolumeEss:
                if (_sb.EssType != EssType.None) {
                    WriteEssVolume(value, _sb.Mixer.Dac);
                    CtmixerUpdateVolumes();
                }
                break;

            case (byte)MixerRegister.MasterVolumeSbPro:
                WriteSbProVolume(_sb.Mixer.Master, value);
                CtmixerUpdateVolumes();
                break;

            case (byte)MixerRegister.FmVolumeSbPro:
                WriteSbProVolume(_sb.Mixer.Fm, value);
                CtmixerUpdateVolumes();
                break;

            case (byte)MixerRegister.CdAudioVolumeSbPro:
                WriteSbProVolume(_sb.Mixer.Cda, value);
                CtmixerUpdateVolumes();
                break;

            case (byte)MixerRegister.LineInVolumeSbPro:
                WriteSbProVolume(_sb.Mixer.Lin, value);
                break;

            case (byte)MixerRegister.MasterVolumeLeft:
                if (_sb.Type == SbType.Sb16) {
                    _sb.Mixer.Master[0] = (byte)(value >> 3);
                    CtmixerUpdateVolumes();
                }
                break;

            case (byte)MixerRegister.MasterVolumeRight:
                if (_sb.Type == SbType.Sb16) {
                    _sb.Mixer.Master[1] = (byte)(value >> 3);
                    CtmixerUpdateVolumes();
                }
                break;

            case (byte)MixerRegister.DacVolumeLeftOrMasterEss:
                if (_sb.Type == SbType.Sb16) {
                    _sb.Mixer.Dac[0] = (byte)(value >> 3);
                    CtmixerUpdateVolumes();
                } else if (_sb.EssType != EssType.None) {
                    WriteEssVolume(value, _sb.Mixer.Master);
                    CtmixerUpdateVolumes();
                }
                break;

            case (byte)MixerRegister.DacVolumeRight:
                if (_sb.Type == SbType.Sb16) {
                    _sb.Mixer.Dac[1] = (byte)(value >> 3);
                    CtmixerUpdateVolumes();
                }
                break;

            case (byte)MixerRegister.FmVolumeLeft:
                if (_sb.Type == SbType.Sb16) {
                    _sb.Mixer.Fm[0] = (byte)(value >> 3);
                    CtmixerUpdateVolumes();
                }
                break;

            case (byte)MixerRegister.FmVolumeRight:
                if (_sb.Type == SbType.Sb16) {
                    _sb.Mixer.Fm[1] = (byte)(value >> 3);
                    CtmixerUpdateVolumes();
                }
                break;

            case (byte)MixerRegister.CdAudioVolumeLeftOrFmEss:
                if (_sb.Type == SbType.Sb16) {
                    _sb.Mixer.Cda[0] = (byte)(value >> 3);
                    CtmixerUpdateVolumes();
                } else if (_sb.EssType != EssType.None) {
                    WriteEssVolume(value, _sb.Mixer.Fm);
                    CtmixerUpdateVolumes();
                }
                break;

            case (byte)MixerRegister.CdAudioVolumeRight:
                if (_sb.Type == SbType.Sb16) {
                    _sb.Mixer.Cda[1] = (byte)(value >> 3);
                    CtmixerUpdateVolumes();
                }
                break;

            case (byte)MixerRegister.LineInVolumeLeftOrCdEss:
                if (_sb.Type == SbType.Sb16) {
                    _sb.Mixer.Lin[0] = (byte)(value >> 3);
                } else if (_sb.EssType != EssType.None) {
                    WriteEssVolume(value, _sb.Mixer.Cda);
                    CtmixerUpdateVolumes();
                }
                break;

            case (byte)MixerRegister.LineInVolumeRight:
                if (_sb.Type == SbType.Sb16) {
                    _sb.Mixer.Lin[1] = (byte)(value >> 3);
                }
                break;

            case (byte)MixerRegister.MicVolume:
                if (_sb.Type == SbType.Sb16) {
                    _sb.Mixer.Mic = (byte)(value >> 3);
                }
                break;

            case (byte)MixerRegister.LineVolumeEss:
                if (_sb.EssType != EssType.None) {
                    WriteEssVolume(value, _sb.Mixer.Lin);
                }
                break;

            case (byte)MixerRegister.IrqSelect:
                _sb.Hw.Irq = 0xFF;
                if ((value & 0x01) != 0) {
                    _sb.Hw.Irq = 2;
                } else if ((value & 0x02) != 0) {
                    _sb.Hw.Irq = 5;
                } else if ((value & 0x04) != 0) {
                    _sb.Hw.Irq = 7;
                } else if ((value & 0x08) != 0) {
                    _sb.Hw.Irq = 10;
                }
                break;

            case (byte)MixerRegister.DmaSelect:
                _sb.Hw.Dma8 = 0xFF;
                _sb.Hw.Dma16 = 0xFF;

                if ((value & 0x01) != 0) {
                    _sb.Hw.Dma8 = 0;
                } else if ((value & 0x02) != 0) {
                    _sb.Hw.Dma8 = 1;
                } else if ((value & 0x08) != 0) {
                    _sb.Hw.Dma8 = 3;
                }

                if ((value & 0x20) != 0) {
                    _sb.Hw.Dma16 = 5;
                } else if ((value & 0x40) != 0) {
                    _sb.Hw.Dma16 = 6;
                } else if ((value & 0x80) != 0) {
                    _sb.Hw.Dma16 = 7;
                }
                break;

            default:
                if (((_sb.Type == SbType.SBPro1 || _sb.Type == SbType.SBPro2) &&
                     _sb.Mixer.Index == 0x0C) ||
                    (_sb.Type == SbType.Sb16 && _sb.Mixer.Index >= 0x3B && _sb.Mixer.Index <= 0x47)) {
                    _sb.Mixer.Unhandled[_sb.Mixer.Index] = value;
                }
                break;
        }
    }

    private byte CtmixerRead() {
        byte ret = 0;

        switch (_sb.Mixer.Index) {
            case (byte)MixerRegister.Reset:
                return 0x00;

            case (byte)MixerRegister.MasterVolumeSb2:
                return (byte)((_sb.Mixer.Master[1] >> 1) & 0x0E);

            case (byte)MixerRegister.Audio1PlayVolumeEss:
                if (_sb.EssType != EssType.None) {
                    ret = ReadEssVolume(_sb.Mixer.Dac);
                }
                break;

            case (byte)MixerRegister.MasterVolumeSbPro:
                return ReadSbProVolume(_sb.Mixer.Master);

            case (byte)MixerRegister.DacVolumeSbPro:
                return ReadSbProVolume(_sb.Mixer.Dac);

            case (byte)MixerRegister.FmOutputSelection:
                return (byte)((_sb.Mixer.Fm[1] >> 1) & 0x0E);

            case (byte)MixerRegister.CdAudioVolumeSb2:
                return (byte)((_sb.Mixer.Cda[1] >> 1) & 0x0E);

            case (byte)MixerRegister.MicLevelOrDacVolume:
                if (_sb.Type == SbType.SB2) {
                    return (byte)(_sb.Mixer.Dac[0] >> 2);
                }
                return (byte)((_sb.Mixer.Mic >> 2) & (_sb.Type == SbType.Sb16 ? 7 : 6));

            case (byte)MixerRegister.OutputStereoSelect:
                return (byte)(0x11 | (_sb.Mixer.StereoEnabled ? 0x02 : 0x00) | (_sb.Mixer.FilterEnabled ? 0x00 : 0x20));

            case (byte)MixerRegister.FmVolumeSbPro:
                return ReadSbProVolume(_sb.Mixer.Fm);

            case (byte)MixerRegister.CdAudioVolumeSbPro:
                return ReadSbProVolume(_sb.Mixer.Cda);

            case (byte)MixerRegister.LineInVolumeSbPro:
                return ReadSbProVolume(_sb.Mixer.Lin);

            case (byte)MixerRegister.MasterVolumeLeft:
                if (_sb.Type == SbType.Sb16) {
                    return (byte)(_sb.Mixer.Master[0] << 3);
                }
                ret = 0x0A;
                break;

            case (byte)MixerRegister.MasterVolumeRight:
                if (_sb.Type == SbType.Sb16) {
                    return (byte)(_sb.Mixer.Master[1] << 3);
                }
                ret = 0x0A;
                break;

            case (byte)MixerRegister.DacVolumeLeftOrMasterEss:
                if (_sb.Type == SbType.Sb16) {
                    return (byte)(_sb.Mixer.Dac[0] << 3);
                }
                if (_sb.EssType != EssType.None) {
                    return ReadEssVolume(_sb.Mixer.Master);
                }
                ret = 0x0A;
                break;

            case (byte)MixerRegister.DacVolumeRight:
                if (_sb.Type == SbType.Sb16) {
                    return (byte)(_sb.Mixer.Dac[1] << 3);
                }
                ret = 0x0A;
                break;

            case (byte)MixerRegister.FmVolumeLeft:
                if (_sb.Type == SbType.Sb16) {
                    return (byte)(_sb.Mixer.Fm[0] << 3);
                }
                ret = 0x0A;
                break;

            case (byte)MixerRegister.FmVolumeRight:
                if (_sb.Type == SbType.Sb16) {
                    return (byte)(_sb.Mixer.Fm[1] << 3);
                }
                ret = 0x0A;
                break;

            case (byte)MixerRegister.CdAudioVolumeLeftOrFmEss:
                if (_sb.Type == SbType.Sb16) {
                    return (byte)(_sb.Mixer.Cda[0] << 3);
                }
                if (_sb.EssType != EssType.None) {
                    return ReadEssVolume(_sb.Mixer.Fm);
                }
                ret = 0x0A;
                break;

            case (byte)MixerRegister.CdAudioVolumeRight:
                if (_sb.Type == SbType.Sb16) {
                    return (byte)(_sb.Mixer.Cda[1] << 3);
                }
                ret = 0x0A;
                break;

            case (byte)MixerRegister.LineInVolumeLeftOrCdEss:
                if (_sb.Type == SbType.Sb16) {
                    return (byte)(_sb.Mixer.Lin[0] << 3);
                }
                if (_sb.EssType != EssType.None) {
                    return ReadEssVolume(_sb.Mixer.Cda);
                }
                ret = 0x0A;
                break;

            case (byte)MixerRegister.LineInVolumeRight:
                if (_sb.Type == SbType.Sb16) {
                    return (byte)(_sb.Mixer.Lin[1] << 3);
                }
                ret = 0x0A;
                break;

            case (byte)MixerRegister.MicVolume:
                if (_sb.Type == SbType.Sb16) {
                    return (byte)(_sb.Mixer.Mic << 3);
                }
                ret = 0x0A;
                break;

            case (byte)MixerRegister.LineVolumeEss:
                if (_sb.EssType != EssType.None) {
                    return ReadEssVolume(_sb.Mixer.Lin);
                }
                ret = 0x0A;
                break;

            case (byte)MixerRegister.IrqSelect:
                if (_sb.Hw.Irq == 2) {
                    ret = 0x01;
                } else if (_sb.Hw.Irq == 5) {
                    ret = 0x02;
                } else if (_sb.Hw.Irq == 7) {
                    ret = 0x04;
                } else if (_sb.Hw.Irq == 10) {
                    ret = 0x08;
                }
                break;

            case (byte)MixerRegister.DmaSelect:
                if (_sb.Hw.Dma8 == 0) {
                    ret |= 0x01;
                } else if (_sb.Hw.Dma8 == 1) {
                    ret |= 0x02;
                } else if (_sb.Hw.Dma8 == 3) {
                    ret |= 0x08;
                }

                if (_sb.Hw.Dma16 == 5) {
                    ret |= 0x20;
                } else if (_sb.Hw.Dma16 == 6) {
                    ret |= 0x40;
                } else if (_sb.Hw.Dma16 == 7) {
                    ret |= 0x80;
                }
                break;

            default:
                if (((_sb.Type == SbType.SBPro1 || _sb.Type == SbType.SBPro2) &&
                     _sb.Mixer.Index == 0x0C) ||
                    (_sb.Type == SbType.Sb16 && _sb.Mixer.Index >= 0x3B && _sb.Mixer.Index <= 0x47)) {
                    ret = _sb.Mixer.Unhandled[_sb.Mixer.Index];
                }
                break;
        }

        return ret;
    }

}