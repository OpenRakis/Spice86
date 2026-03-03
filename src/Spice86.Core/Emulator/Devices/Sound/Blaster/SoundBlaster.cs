namespace Spice86.Core.Emulator.Devices.Sound.Blaster;

using Serilog.Events;

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
        mixer.RegisterQueueNotifier(this);
        mixer.LockMixerThread();

        _config = soundBlasterHardwareConfig;
        _dualPic = dualPic;
        _mixer = mixer;
        _opl = opl;
        _scheduler = scheduler;
        _clock = clock;
        _sb = new SbInfo(clock);
        _dmaBus = dmaBus;

        _perTickHandler = (_) => PerTickCallback();
        _perFrameHandler = (_) => PerFrameCallback();

        _sb.Hw.Base = _config.BaseAddress;
        _sb.Hw.Irq = _config.Irq;

        const int ColdWarmupMs = 100;
        _sb.Dsp.ColdWarmupMs = ColdWarmupMs;
        // Magic 32 divisor was probably the result of experimentation
        _sb.Dsp.HotWarmupMs = ColdWarmupMs / 32;

        _sb.Mixer.Enabled = soundBlasterHardwareConfig.OplConfig.SbMixer;
        _sb.Mixer.StereoEnabled = false;

        _sb.Type = _config.SbType;

        _sb.Hw.Dma8 = _config.LowDma;

        DmaChannel? primaryDmaChannel = dmaBus.GetChannel(_config.LowDma)
            ?? throw new InvalidOperationException($"DMA channel {_config.LowDma} unavailable for Sound Blaster.");
        primaryDmaChannel.ReserveFor("SoundBlaster", OnDmaChannelEvicted);

        // Only Sound Blaster 16 uses a 16-bit DMA channel.
        if (_sb.Type == SbType.Sb16) {
            _sb.Hw.Dma16 = _config.HighDma;
            // Reserve the second DMA channel only if it's unique.
            if (ShouldUseHighDmaChannel()) {
                DmaChannel? secondaryDmaChannel = dmaBus.GetChannel(_config.HighDma);
                secondaryDmaChannel?.ReserveFor("SoundBlaster16", OnDmaChannelEvicted);
            }
        }

        HashSet<ChannelFeature> dacFeatures = new HashSet<ChannelFeature> {
            ChannelFeature.ReverbSend,
            ChannelFeature.ChorusSend,
            ChannelFeature.DigitalAudio,
            ChannelFeature.Sleep
        };
        if (_sb.Type is SbType.SBPro1 or SbType.SBPro2 or SbType.Sb16) {
            dacFeatures.Add(ChannelFeature.Stereo);
        }

        _dacChannel = _mixer.AddChannel(
            framesRequested => MixerCallback(framesRequested),
            DefaultPlaybackRateHz,
            "SoundBlasterDAC",
            dacFeatures);

        if (_sb.Type == SbType.SBPro2) {
            _dacChannel.SetZeroOrderHoldUpsamplerTargetRate(NativeDacRateHz);
            _dacChannel.SetResampleMethod(ResampleMethod.ZeroOrderHoldAndResample);
        }

        _sb.Dsp.State = DspState.Normal;
        _sb.Dsp.Out.LastVal = 0xAA;
        _sb.Dma.Channel = null;

        InitPortHandlers(ioPortDispatcher);

        _aspRegs[5] = 0x01;
        _aspRegs[9] = 0xF8;

        DspReset();
        CtmixerReset();

        _dualPic.SetIrqMask(_config.Irq, false);

        if (this._loggerService.IsEnabled(LogEventLevel.Information)) {
            if (_sb.Type == SbType.Sb16) {
                this._loggerService.Information(
                    "SoundBlaster: Running on port {Port:X3}h, IRQ {Irq}, DMA {Dma8}, and high DMA {Dma16}",
                    _sb.Hw.Base, _sb.Hw.Irq, _sb.Hw.Dma8, _sb.Hw.Dma16);
            } else {
                this._loggerService.Information(
                    "SoundBlaster: Running on port {Port:X3}h, IRQ {Irq}, and DMA {Dma8}",
                    _sb.Hw.Base, _sb.Hw.Irq, _sb.Hw.Dma8);
            }
        }

        // Size to 2x blocksize. The mixer callback will request 1x blocksize.
        // This provides a good size to avoid over-runs and stalls.
        _outputQueue.Resize((int)Math.Ceiling(_dacChannel.FramesPerBlock * 2.0f));

        mixer.UnlockMixerThread();
    }

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
        byte result;
        int offset = port - _config.BaseAddress;
        switch (offset) {
            case 0x04: // MixerIndex
                result = _sb.Mixer.Index;
                return result;

            case 0x05: // MixerData
                result = CtmixerRead();
                return result;

            case 0x0A: // DspReadData
                result = DspReadData();
                return result;

            case 0x0C: { // DspWriteStatus
                    // Bit 7 = 1 means buffer at capacity (not ready to receive).
                    // Lower 7 bits are always 1.
                    byte writeStatus = 0x7F; // lower 7 bits always set
                    if (WriteBufferAtCapacity()) {
                        writeStatus |= 0x80;
                    }
                    return writeStatus;
                }

            case 0x0E: { // DspReadStatus
                    // Acknowledges 8-bit IRQ. Bit 7 = 1 if output FIFO has data.
                    // Lower 7 bits are always 1.
                    if (_sb.Irq.Pending8Bit) {
                        _sb.Irq.Pending8Bit = false;
                        _dualPic.DeactivateIrq(_sb.Hw.Irq);
                    }
                    byte readStatus = 0x7F; // lower 7 bits always set
                    if (_sb.Dsp.Out.Used != 0) {
                        readStatus |= 0x80;
                    }
                    return readStatus;
                }

            case 0x0F: // DspAck16Bit
                _sb.Irq.Pending16Bit = false;
                return 0xFF;

            case 0x06: // DspReset read
                return 0xFF;

            default:
                return 0xFF;
        }
    }

    public override void WriteByte(ushort port, byte value) {
        int offset = port - _config.BaseAddress;
        switch (offset) {
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
                base.WriteByte(port, value);
                break;
        }
    }

    public override void WriteWord(ushort port, ushort value) {
        int offset = port - _config.BaseAddress;
        if (offset is < 4 or > 0xF) {
            base.WriteWord(port, value);
            return;
        }
        WriteByte(port, (byte)(value & 0xFF));
        WriteByte((ushort)(port + 1), (byte)(value >> 8));
    }

    public override ushort ReadWord(ushort port) {
        int offset = port - _config.BaseAddress;
        if (offset is < 4 or > 0xF) {
            return base.ReadWord(port);
        }
        byte low = ReadByte(port);
        byte high = ReadByte((ushort)(port + 1));
        ushort result = (ushort)(low | (high << 8));
        return result;
    }

    /// <inheritdoc />
    public void NotifyLockMixer() {
        _outputQueue.Stop();
    }

    /// <inheritdoc />
    public void NotifyUnlockMixer() {
        _outputQueue.Start();
    }


    /// <summary>
    /// Event callback for delayed IRQ raising.
    /// </summary>
    private void DspRaiseIrqEvent(uint val) {
        RaiseIrq(SbIrq.Irq8);
    }

    public void RaiseInterruptRequest() {
        RaiseIrq(SbIrq.Irq8);
    }

    /// <summary>
    /// Gets the configured Sound Blaster type.
    /// </summary>
    public SbType SbTypeProperty => _sb.Type;

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
            return $"A{_config.BaseAddress:X3} I{_config.Irq} D{_config.LowDma}{highChannelSegment} T{(int)_sb.Type}";
        }
    }

    /// <summary>
    /// Called by the mixer thread to pull audio frames from the output queue.
    /// Sets frames_needed so the emulation-thread callbacks know how much to generate,
    /// then delegates to the generic queue-pull helper.
    /// </summary>
    private void MixerCallback(int framesRequested) {
        int queueSize = _outputQueue.Size;
        int needed = Math.Max(framesRequested - queueSize, 0);
        System.Threading.Interlocked.Exchange(ref _framesNeeded, needed);

        SoftwareMixer.PullFromQueueCallback<SoundBlaster, AudioFrame>(framesRequested, this);
    }

    /// <summary>
    ///  Wake up the queue and channel to resume processing and playback
    /// </summary>
    private bool MaybeWakeUp() {
        _outputQueue.Start();
        bool wokeUp = _dacChannel.WakeUp();
        if (wokeUp && _loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("SB: DAC channel woke up");
        }
        return wokeUp;
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
                    // Catch up to current time, but don't generate an IRQ!
                    // Fixes problems with later sci games.
                    double t = _clock.ElapsedTimeMs - _lastDmaCallbackTime;
                    uint s = (uint)(_sb.Dma.Rate * t / 1000.0);

                    if (s > _sb.Dma.Min) {
                        s = _sb.Dma.Min;
                    }

                    uint minSize = _sb.Dma.Mul >> SbShift;
                    if (minSize == 0) {
                        minSize = 1;
                    }
                    minSize *= 2;

                    if (_sb.Dma.Left > minSize) {
                        if (s > (_sb.Dma.Left - minSize)) {
                            s = _sb.Dma.Left - minSize;
                        }
                        // This will trigger an irq, see
                        // PlayDmaTransfer, so lets not do that
                        if (!_sb.Dma.AutoInit && _sb.Dma.Left <= _sb.Dma.Min) {
                            s = 0;
                        }
                        if (s > 0) {
                            PlayDmaTransfer(s);
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

    private static byte DecodeAdpcmPortion(
        int bitPortion,
        ReadOnlySpan<byte> adjustMap,
        ReadOnlySpan<sbyte> scaleMap,
        int lastIndex,
        ref byte sample,
        ref ushort scale) {
        int i = Math.Clamp(bitPortion + scale, 0, lastIndex);
        scale = (ushort)((scale + adjustMap[i]) & 0xff);
        sample = (byte)Math.Clamp(sample + scaleMap[i], 0, 255);
        return sample;
    }

    private static byte[] DecodeAdpcm2Bit(byte data, ref byte reference, ref ushort stepsize) {
        ReadOnlySpan<sbyte> scaleMap = [
             0,  1,  0,  -1,  1,  3,  -1,  -3,
             2,  6, -2,  -6,  4, 12,  -4, -12,
             8, 24, -8, -24,  6, 48, -16, -48
        ];
        ReadOnlySpan<byte> adjustMap = [
              0,   4,   0,   4,
            252,   4, 252,   4, 252,   4, 252,   4,
            252,   4, 252,   4, 252,   4, 252,   4,
            252,   0, 252,   0
        ];
        const int lastIndex = 23;

        byte[] samples =
        [
            DecodeAdpcmPortion((data >> 6) & 0x3, adjustMap, scaleMap, lastIndex, ref reference, ref stepsize),
            DecodeAdpcmPortion((data >> 4) & 0x3, adjustMap, scaleMap, lastIndex, ref reference, ref stepsize),
            DecodeAdpcmPortion((data >> 2) & 0x3, adjustMap, scaleMap, lastIndex, ref reference, ref stepsize),
            DecodeAdpcmPortion((data >> 0) & 0x3, adjustMap, scaleMap, lastIndex, ref reference, ref stepsize),
        ];
        return samples;
    }

    private static byte[] DecodeAdpcm3Bit(byte data, ref byte reference, ref ushort stepsize) {
        ReadOnlySpan<sbyte> scaleMap = [
             0,  1,  2,  3,  0,  -1,  -2,  -3,
             1,  3,  5,  7, -1,  -3,  -5,  -7,
             2,  6, 10, 14, -2,  -6, -10, -14,
             4, 12, 20, 28, -4, -12, -20, -28,
             5, 15, 25, 35, -5, -15, -25, -35
        ];
        ReadOnlySpan<byte> adjustMap = [
              0, 0, 0,   8,   0, 0, 0,   8,
            248, 0, 0,   8, 248, 0, 0,   8,
            248, 0, 0,   8, 248, 0, 0,   8,
            248, 0, 0,   8, 248, 0, 0,   8,
            248, 0, 0,   0, 248, 0, 0,   0
        ];
        const int lastIndex = 39;

        byte[] samples =
        [
            DecodeAdpcmPortion((data >> 5) & 0x7, adjustMap, scaleMap, lastIndex, ref reference, ref stepsize),
            DecodeAdpcmPortion((data >> 2) & 0x7, adjustMap, scaleMap, lastIndex, ref reference, ref stepsize),
            DecodeAdpcmPortion((data & 0x3) << 1, adjustMap, scaleMap, lastIndex, ref reference, ref stepsize),
        ];
        return samples;
    }

    private static byte[] DecodeAdpcm4Bit(byte data, ref byte reference, ref ushort stepsize) {
        ReadOnlySpan<sbyte> scaleMap = [
             0,  1,  2,  3,  4,  5,  6,  7,  0,  -1,  -2,  -3,  -4,  -5,  -6,  -7,
             1,  3,  5,  7,  9, 11, 13, 15, -1,  -3,  -5,  -7,  -9, -11, -13, -15,
             2,  6, 10, 14, 18, 22, 26, 30, -2,  -6, -10, -14, -18, -22, -26, -30,
             4, 12, 20, 28, 36, 44, 52, 60, -4, -12, -20, -28, -36, -44, -52, -60
        ];
        ReadOnlySpan<byte> adjustMap = [
              0, 0, 0, 0, 0, 16, 16, 16,
              0, 0, 0, 0, 0, 16, 16, 16,
            240, 0, 0, 0, 0, 16, 16, 16,
            240, 0, 0, 0, 0, 16, 16, 16,
            240, 0, 0, 0, 0, 16, 16, 16,
            240, 0, 0, 0, 0, 16, 16, 16,
            240, 0, 0, 0, 0,  0,  0,  0,
            240, 0, 0, 0, 0,  0,  0,  0
        ];
        const int lastIndex = 63;

        byte[] samples =
        [
            DecodeAdpcmPortion(data >> 4, adjustMap, scaleMap, lastIndex, ref reference, ref stepsize),
            DecodeAdpcmPortion(data & 0xF, adjustMap, scaleMap, lastIndex, ref reference, ref stepsize),
        ];
        return samples;
    }

    private static (byte[], byte, ushort) DecodeAdpcm2Bit(byte data, byte reference, ushort stepsize) {
        byte refCopy = reference;
        ushort stepsizeCopy = stepsize;
        byte[] samples = DecodeAdpcm2Bit(data, ref refCopy, ref stepsizeCopy);
        return (samples, refCopy, stepsizeCopy);
    }

    private static (byte[], byte, ushort) DecodeAdpcm3Bit(byte data, byte reference, ushort stepsize) {
        byte refCopy = reference;
        ushort stepsizeCopy = stepsize;
        byte[] samples = DecodeAdpcm3Bit(data, ref refCopy, ref stepsizeCopy);
        return (samples, refCopy, stepsizeCopy);
    }

    private static (byte[], byte, ushort) DecodeAdpcm4Bit(byte data, byte reference, ushort stepsize) {
        byte refCopy = reference;
        ushort stepsizeCopy = stepsize;
        byte[] samples = DecodeAdpcm4Bit(data, ref refCopy, ref stepsizeCopy);
        return (samples, refCopy, stepsizeCopy);
    }

    private uint ReadDma8Bit(uint bytesToRead, uint bufferIndex = 0) {
        if (bufferIndex >= DmaBufSize || _sb.Dma.Channel is null) {
            // Should never happen as the code is currently written.
            // Calling code has buffer_index either 0 or 1 to handle a
            // dangling sample from the last read. This is to solve an edge
            // case for stereo sound when the DMA buffer has an odd number
            // of samples.
            return 0;
        }
        uint bytesAvailable = DmaBufSize - bufferIndex;
        uint clampedBytes = Math.Min(bytesToRead, bytesAvailable);

        Span<byte> buffer = _sb.Dma.Buf8.AsSpan((int)bufferIndex, (int)clampedBytes);
        uint bytesRead = (uint)_sb.Dma.Channel.Read((int)clampedBytes, buffer);
        return bytesRead;
    }

    private uint ReadDma16Bit(uint wordsToRead, uint bufferIndex = 0) {
        if (bufferIndex >= DmaBufSize || _sb.Dma.Channel is null) {
            return 0;
        }

        // In DMA controller, if channel is 16-bit, we're dealing with 16-bit words.
        // Otherwise, we're dealing with 8-bit words (bytes).
        // Calling code handles this case and conditionally divides by two.
        bool is16BitChannel = _sb.Dma.Channel.Is16Bit;
        uint bytesRequested = wordsToRead;
        if (is16BitChannel) {
            bytesRequested *= 2;
            if (_sb.Dma.Mode != DmaMode.Pcm16Bit) {
                _loggerService.Warning("SOUNDBLASTER: Expected 16-bit mode but DMA mode is {Mode}", _sb.Dma.Mode);
            }
        } else {
            if (_sb.Dma.Mode != DmaMode.Pcm16BitAliased) {
                _loggerService.Warning("SOUNDBLASTER: Expected 16-bit aliased mode but DMA mode is {Mode}", _sb.Dma.Mode);
            }
        }

        // Clamp words to read so we don't overflow our buffer
        uint bytesAvailable = (DmaBufSize - bufferIndex) * 2;
        uint clampedWords = Math.Min(bytesRequested, bytesAvailable);
        if (is16BitChannel) {
            clampedWords /= 2;
        }
        Span<byte> unsignedBuf = System.Runtime.InteropServices.MemoryMarshal.Cast<short, byte>(_sb.Dma.Buf16.AsSpan((int)bufferIndex));
        uint wordsRead = (uint)_sb.Dma.Channel.Read((int)clampedWords, unsignedBuf);
        return wordsRead;
    }

    private void DspE2DmaCallback(DmaChannel channel, DmaChannel.DmaEvent dmaEvent) {
        if (dmaEvent == DmaChannel.DmaEvent.IsUnmasked) {
            byte val = (byte)(_sb.E2.Value & 0xff);

            // Unregister callback and write the E2 value
            channel.RegisterCallback(null);
            Span<byte> buffer = [val];
            channel.Write(1, buffer);
        }
    }

    private void DspAdcCallback(DmaChannel channel, DmaChannel.DmaEvent dmaEvent) {
        if (dmaEvent != DmaChannel.DmaEvent.IsUnmasked) {
            return;
        }

        Span<byte> buffer = [128];
        while (_sb.Dma.Left-- > 0) {
            channel.Write(1, buffer);
        }

        RaiseIrq(SbIrq.Irq8);
        channel.RegisterCallback(null);
    }

    private void PlayDmaTransfer(uint bytesRequested) {
        // How many bytes should we read from DMA?
        uint lowerBound = _sb.Dma.AutoInit ? bytesRequested : _sb.Dma.Min;
        uint bytesToRead = _sb.Dma.Left <= lowerBound ? _sb.Dma.Left : bytesRequested;

        // All three of these must be populated during the DMA sequence to
        // ensure the proper quantities and unit are being accounted for.
        // For example: use the channel count to convert from samples to frames.
        uint bytesRead = 0;
        uint samples = 0;
        ushort frames = 0;

        // In DmaMode.Pcm16BitAliased mode temporarily divide by 2 to get
        // number of 16-bit samples, because 8-bit DMA Read returns byte size,
        // while in DmaMode.Pcm16Bit mode 16-bit DMA Read returns word size.
        byte dma16ToSampleDivisor = _sb.Dma.Mode == DmaMode.Pcm16BitAliased ? (byte)2 : (byte)1;

        // Used to convert from samples to frames (which is what AddSamples unintuitively uses..)
        byte channels = _sb.Dma.Stereo ? (byte)2 : (byte)1;

        _lastDmaCallbackTime = _clock.ElapsedTimeMs;

        // Read the actual data, process it and send it off to the mixer
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

                    // Only add whole frames when in stereo DMA mode. The
                    // number of frames comes from the DMA request, and
                    // therefore user-space data.
                    if (frames > 0) {
                        if (_sb.Dma.Sign) {
                            EnqueueFramesStereo(_sb.Dma.Buf8, samples, true);
                        } else {
                            EnqueueFramesStereo(_sb.Dma.Buf8, samples, false);
                        }
                    }
                    // Otherwise there's an unhandled dangling sample from the last round
                    if ((samples & 1) != 0) {
                        _sb.Dma.RemainSize = 1;
                        _sb.Dma.Buf8[0] = _sb.Dma.Buf8[samples - 1];
                    } else {
                        _sb.Dma.RemainSize = 0;
                    }
                } else { // Mono
                    bytesRead = ReadDma8Bit(bytesToRead);
                    samples = bytesRead;
                    // mono sanity-check
                    if (channels != 1) {
                        _loggerService.Error("SOUNDBLASTER: Mono mode but channels={Channels}", channels);
                    }
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

                    // Only add whole frames when in stereo DMA mode
                    if (frames > 0) {
                        if (_sb.Dma.Sign) {
                            EnqueueFramesStereo16(_sb.Dma.Buf16, samples, true);
                        } else {
                            EnqueueFramesStereo16(_sb.Dma.Buf16, samples, false);
                        }
                    }
                    if ((samples & 1) != 0) {
                        // Carry over the dangling sample into the next round, or...
                        _sb.Dma.RemainSize = 1;
                        _sb.Dma.Buf16[0] = _sb.Dma.Buf16[samples - 1];
                    } else {
                        // ...the DMA transfer is done
                        _sb.Dma.RemainSize = 0;
                    }
                } else { // 16-bit mono
                    bytesRead = ReadDma16Bit(bytesToRead);
                    samples = bytesRead / dma16ToSampleDivisor;

                    // mono sanity check
                    if (channels != 1) {
                        _loggerService.Error("SOUNDBLASTER: Mono mode but channels={Channels}", channels);
                    }

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

        // Sanity check
        if (frames > samples) {
            _loggerService.Error("SOUNDBLASTER: Frames {Frames} should never exceed samples {Samples}", frames, samples);
        }

        // If the first DMA transfer after a reset contains a single sample, it
        // should be ignored. Quake and SBTEST.EXE have this behavior. If not
        // ignored, the channels will be incorrectly reversed.
        // https://github.com/dosbox-staging/dosbox-staging/issues/2942
        // https://www.vogons.org/viewtopic.php?p=536104#p536104
        if (_sb.Dma.FirstTransfer && samples == 1) {
            // Forget any "dangling sample" that would otherwise be carried
            // over to the next transfer.
            _sb.Dma.RemainSize = 0;
        }
        _sb.Dma.FirstTransfer = false;

        // Deduct the DMA bytes read from the remaining to still read
        _sb.Dma.Left -= bytesRead;

        if (_sb.Dma.Left == 0) {
            _scheduler.RemoveEvents(ProcessDmaTransferEvent);

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
        ushort numFrames = (ushort)numSamples;
        return (numBytes, numSamples, numFrames);
    }

    private void EnqueueFramesMono(byte[] samples, uint numSamples, bool signed) {
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

        // Batch process samples into AudioFrames and enqueue to output_queue
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

    private void EnqueueSilentFrames(uint count) {
        _enqueueBatchCount = 0;
        AudioFrame silence = new AudioFrame(0.0f, 0.0f);
        for (uint i = 0; i < count; i++) {
            _enqueueBatch[_enqueueBatchCount++] = silence;
        }
        FlushEnqueueBatch();
        _framesAddedThisTick += (int)count;
    }

    private void FlushEnqueueBatch() {
        if (_enqueueBatchCount == 0) {
            return;
        }
        _outputQueue.NonblockingBulkEnqueue(_enqueueBatch.AsSpan(0, _enqueueBatchCount));
        _enqueueBatchCount = 0;
    }

    private void EnqueueFramesStereo(byte[] samples, uint numSamples, bool signed) {
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
        bool swapChannels = _sb.Type is SbType.SBPro1 or SbType.SBPro2;

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

    private void EnqueueFramesMono16(short[] samples, uint numSamples, bool signed) {
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

    private void EnqueueFramesStereo16(short[] samples, uint numSamples, bool signed) {
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
        bool swapChannels = _sb.Type is SbType.SBPro1 or SbType.SBPro2;

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

    private void RaiseIrq(SbIrq irqType) {
        switch (irqType) {
            case SbIrq.Irq8:
                // Don't raise if already pending
                if (_sb.Irq.Pending8Bit) {
                    return;
                }
                _sb.Irq.Pending8Bit = true;
                _dualPic.ActivateIrq(_sb.Hw.Irq);
                break;

            case SbIrq.Irq16:
                // Don't raise if already pending
                if (_sb.Irq.Pending16Bit) {
                    return;
                }
                _sb.Irq.Pending16Bit = true;
                _dualPic.ActivateIrq(_sb.Hw.Irq);
                break;

            case SbIrq.IrqMpu:
                // MPU-401 IRQ handling not implemented yet
                _loggerService.Warning("SOUNDBLASTER: MPU-401 IRQ not yet implemented");
                break;
        }
    }

    private void DspDoReset(byte value) {
        if (((value & 1) != 0) && (_sb.Dsp.State != DspState.Reset)) {
            // TODO Get out of highspeed mode
            DspReset();
            _sb.Dsp.State = DspState.Reset;
        } else if (((value & 1) == 0) && (_sb.Dsp.State == DspState.Reset)) {
            // reset off
            _sb.Dsp.State = DspState.ResetWait;
            _scheduler.RemoveEvents(DspFinishResetEvent);
            _scheduler.AddEvent(DspFinishResetEvent, 20.0 / 1000.0, 0);  // 20 microseconds = 0.020 ms
            _loggerService.Information("Resetting SB DSP");
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
        _dualPic.DeactivateIrq(_sb.Hw.Irq);
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

        _sb.Adpcm = new();
        _sb.Adpcm.Reference = 0;
        _sb.Adpcm.Stepsize = 0;
        _sb.Adpcm.HaveRef = false;
        _sb.Dac = new(_sb, _clock);

        _sb.FreqHz = DefaultPlaybackRateHz;
        _sb.TimeConstant = 45;
        _sb.E2.Value = 0xaa;
        _sb.E2.Count = 0;

        _sb.Irq.Pending8Bit = false;
        _sb.Irq.Pending16Bit = false;

        SetChannelRateHz(DefaultPlaybackRateHz);
        InitSpeakerState();
        _scheduler.RemoveEvents(ProcessDmaTransferEvent);
    }

    private void DspDoWrite(byte value) {
        switch (_sb.Dsp.Cmd) {
            case (byte)BlasterState.WaitingForCommand:
                _sb.Dsp.Cmd = value;
                if (_sb.Type == SbType.Sb16) {
                    _sb.Dsp.CmdLen = DspCommandLengthsSb16[value];
                } else {
                    _sb.Dsp.CmdLen = DspCommandLengthsSb[value];
                }
                _sb.Dsp.In.Pos = 0;
                if (_sb.Dsp.CmdLen == 0) {
                    DspDoCommand();
                }
                break;

            default:
                _sb.Dsp.In.Data[_sb.Dsp.In.Pos] = value;
                _sb.Dsp.In.Pos++;
                if (_sb.Dsp.In.Pos >= _sb.Dsp.CmdLen) {
                    DspDoCommand();
                }
                break;
        }
    }

    private void DspDoCommand() {
        switch (_sb.Dsp.Cmd) {
            case 0x04:
                if (_sb.Type == SbType.Sb16) {
                    // Sb16 ASP set mode register or DSP Status
                    if ((_sb.Dsp.In.Data[0] & 0xf1) == 0xf1) {
                        _aspInitInProgress = true;
                    } else {
                        _aspInitInProgress = false;
                    }
                } else {
                    // DSP Status SB 2.0/pro version. NOT SB16.
                    DspFlushData();
                    if (_sb.Type == SbType.SB2) {
                        DspAddData(0x88);
                    } else if (_sb.Type is SbType.SBPro1 or SbType.SBPro2) {
                        DspAddData(0x7b);
                    } else {
                        // Everything enabled
                        DspAddData(0xff);
                    }
                }
                break;

            case 0x05: // SB16 ASP set codec parameter
                break;

            case 0x08: // SB16 ASP get version
                if (_sb.Type == SbType.Sb16) {
                    switch (_sb.Dsp.In.Data[0]) {
                        case 0x03:
                            DspAddData(0x18); // version ID (??)
                            break;

                        default:
                            break;
                    }
                }
                break;

            case 0x0e:
                // Sb16 ASP set register
                if (_sb.Type == SbType.Sb16) {
                    _aspRegs[_sb.Dsp.In.Data[0]] = _sb.Dsp.In.Data[1];
                }
                break;

            case 0x0f:
                // Sb16 ASP get register
                if (_sb.Type == SbType.Sb16) {
                    if (_aspInitInProgress && (_sb.Dsp.In.Data[0] == 0x83)) {
                        _aspRegs[0x83] = (byte)~_aspRegs[0x83];
                    }
                    DspAddData(_aspRegs[_sb.Dsp.In.Data[0]]);
                }
                break;

            case 0x10:
                // Direct DAC
                DspChangeMode(DspMode.Dac);
                if (MaybeWakeUp()) {
                    // If we're waking up, then the DAC hasn't been running
                    // (or maybe wasn't running at all), so start with a
                    // fresh DAC state.
                    _sb.Dac = new(_sb, _clock);
                }

                // Ensure we're using per-frame callback timing because DAC samples
                // are sent one after another with sub-millisecond timing.
                SetCallbackPerFrame();

                int? dacRateHz = _sb.Dac.MeasureDacRateHz();
                if (dacRateHz.HasValue) {
                    SetChannelRateHz(dacRateHz.Value);
                }
                break;

            case 0x24:
                // Single Cycle 8-Bit DMA ADC
                _sb.Dma.Left = (uint)(1 + _sb.Dsp.In.Data[0] + (_sb.Dsp.In.Data[1] << 8));
                _sb.Dma.Sign = false;
                _dmaBus.GetChannel(_sb.Hw.Dma8)?.RegisterCallback(DspAdcCallback);
                break;

            case 0x14: // Single Cycle 8-Bit DMA DAC
                goto case 0x15;

            case 0x15:
                // Wari hack. Waru uses this one instead of 0x14, but some
                // weird stuff going on there anyway
                goto case 0x91;

            case 0x91:
                // Singe Cycle 8-Bit DMA High speed DAC
                // Note: 0x91 is documented only for DSP ver.2.x and 3.x,
                // not 4.x
                DspPrepareDmaOld(DmaMode.Pcm8Bit, false, false);
                break;

            case 0x1c: // Auto Init 8-bit DMA
            case 0x90: // Auto Init 8-bit DMA High Speed
                // Note: 0x90 is documented only for DSP ver.2.x and 3.x,
                // not 4.x
                if (_sb.Type > SbType.SB1) {
                    DspPrepareDmaOld(DmaMode.Pcm8Bit, true, false);
                }
                break;

            case 0x38:
                // Write to SB MIDI Output
                if (_sb.MidiEnabled) {
                    // TODO: Forward to MIDI subsystem
                }
                break;

            case 0x40:
                // Set Timeconstant
                DspChangeRate((uint)(1000000 / (256 - _sb.Dsp.In.Data[0])));
                break;

            case 0x41: // Set Output/Input Samplerate
            case 0x42:
                // Note: 0x42 is handled like 0x41, needed by Fasttracker II
                if (_sb.Type == SbType.Sb16) {
                    uint rate = (uint)((_sb.Dsp.In.Data[0] << 8) | _sb.Dsp.In.Data[1]);
                    DspChangeRate(rate);
                } else {
                    if (_loggerService.IsEnabled(LogEventLevel.Information)) {
                        _loggerService.Information("SB: Game uses SB16 sample rate command 0x{Cmd:X2} but running as {SbType}",
                            _sb.Dsp.Cmd, _sb.Type);
                    }
                }
                break;

            case 0x48:
                // Set DMA Block Size
                if (_sb.Type > SbType.SB1) {
                    _sb.Dma.AutoSize = (uint)(1 + _sb.Dsp.In.Data[0] + (_sb.Dsp.In.Data[1] << 8));
                }
                break;

            case 0x75:  // 075h : Single Cycle 4-bit ADPCM Reference
                _sb.Adpcm.HaveRef = true;
                goto case 0x74;

            case 0x74:  // 074h : Single Cycle 4-bit ADPCM
                DspPrepareDmaOld(DmaMode.Adpcm4Bit, false, false);
                break;

            case 0x77: // 077h : Single Cycle 3-bit(2.6bit) ADPCM Reference
                _sb.Adpcm.HaveRef = true;
                goto case 0x76;

            case 0x76: // 076h : Single Cycle 3-bit(2.6bit) ADPCM
                DspPrepareDmaOld(DmaMode.Adpcm3Bit, false, false);
                break;

            case 0x7d: // Auto Init 4-bit ADPCM Reference
                if (_sb.Type > SbType.SB1) {
                    _sb.Adpcm.HaveRef = true;
                    DspPrepareDmaOld(DmaMode.Adpcm4Bit, true, false);
                }
                break;

            case 0x17:  // 017h : Single Cycle 2-bit ADPCM Reference
                _sb.Adpcm.HaveRef = true;
                goto case 0x16;

            case 0x16: // 016h : Single Cycle 2-bit ADPCM
                DspPrepareDmaOld(DmaMode.Adpcm2Bit, false, false);
                break;

            case 0x80: // Silence DAC
                {
                    uint samples = (uint)(1 + _sb.Dsp.In.Data[0] + (_sb.Dsp.In.Data[1] << 8));
                    double delayMs = (1000.0 * samples) / _sb.FreqHz;
                    _scheduler.AddEvent(DspRaiseIrqEvent, delayMs, 0);
                }
                break;

            // Generic 8/16-bit DMA commands (SB16 only) - 0xB0-0xCF
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
                // Generic 8/16 bit DMA commands (SB16 only)
                if (_sb.Type != SbType.Sb16) {
                    break;
                }
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
                DspPrepareDmaNew(is16Bit ? DmaMode.Pcm16Bit : DmaMode.Pcm8Bit, length, autoInit, stereo);
                break;

            case 0xd5:
                // Halt 16-bit DMA
                if (_sb.Type == SbType.Sb16) {
                    goto case 0xd0;
                }
                break;

            case 0xd0:
                // Halt 8-bit DMA
                _sb.Mode = DspMode.DmaPause;
                _scheduler.RemoveEvents(ProcessDmaTransferEvent);
                break;

            case 0xd1:
                // Enable Speaker
                SetSpeakerEnabled(true);
                break;

            case 0xd3:
                // Disable Speaker
                SetSpeakerEnabled(false);
                break;

            case 0xd8:
                // Speaker status
                if (_sb.Type <= SbType.SB1) {
                    break;
                }
                DspFlushData();
                if (_sb.SpeakerEnabled) {
                    DspAddData(0xff);
                    // If the game is courteous enough to ask if the speaker
                    // is ready, then we can be confident it won't play
                    // garbage content, so we zero the warmup count down.
                    _sb.Dsp.WarmupRemainingMs = 0;
                } else {
                    DspAddData(0x00);
                }
                break;

            case 0xd6:
                // Continue DMA 16-bit
                if (_sb.Type != SbType.Sb16) {
                    break;
                }
                goto case 0xd4;

            case 0xd4:
                // Continue DMA 8-bit
                if (_sb.Mode == DspMode.DmaPause) {
                    _sb.Mode = DspMode.DmaMasked;
                    _sb.Dma.Channel?.RegisterCallback(DspDmaCallback);
                }
                break;

            case 0xd9:
                // Exit Autoinitialize 16-bit
                if (_sb.Type == SbType.Sb16) {
                    goto case 0xda;
                }
                break;

            case 0xda:
                // Exit Autoinitialize 8-bit
                if (_sb.Type > SbType.SB1) {
                    // Should stop itself
                    _sb.Dma.AutoInit = false;
                }
                break;

            case 0xe0: // DSP Identification - SB2.0+
                DspFlushData();
                DspAddData((byte)~_sb.Dsp.In.Data[0]);
                break;

            case 0xe1:
                // Get DSP Version
                DspFlushData();
                switch (_sb.Type) {
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
                // Weird DMA identification write routine
                for (int i = 0; i < 8; i++) {
                    if (((_sb.Dsp.In.Data[0] >> i) & 0x01) != 0) {
                        _sb.E2.Value += E2IncrTable[_sb.E2.Count % 4][i];
                    }
                }
                _sb.E2.Value += E2IncrTable[_sb.E2.Count % 4][8];
                _sb.E2.Count++;
                // Register callback to write E2 value when DMA is unmasked
                _dmaBus.GetChannel(_sb.Hw.Dma8)?.RegisterCallback(DspE2DmaCallback);
                break;

            case 0xe3:
                // DSP Copyright
                DspFlushData();
                if (_sb.EssType != EssType.None) {
                    DspAddData(0);
                } else {
                    const string copyright = "COPYRIGHT (C) CREATIVE TECHNOLOGY LTD, 1992.";
                    foreach (char c in copyright) {
                        DspAddData((byte)c);
                    }
                }
                break;

            case 0xe4:
                // Write Test Register
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
                // Trigger 8bit IRQ
                // Small delay to emulate DSP slowness, fixes Llamatron 2012 and Lemmings 3D
                _scheduler.AddEvent(DspRaiseIrqEvent, 0.01, 0);
                break;

            case 0xf3:
                // Trigger 16bit IRQ
                if (_sb.Type == SbType.Sb16) {
                    RaiseIrq(SbIrq.Irq16);
                }
                break;

            case 0xf8:
                // Undocumented, pre-Sb16 only
                DspFlushData();
                DspAddData(0);
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
                if (_sb.Type > SbType.SB1) {
                    if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                        _loggerService.Error("DSP:Unimplemented MIDI UART command {Cmd:X2}",
                                            _sb.Dsp.Cmd);
                    }
                }
                break;

            case 0x7f:
            case 0x1f:
                if (_sb.Type > SbType.SB1) {
                    if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                        _loggerService.Error("DSP:Unimplemented auto-init DMA ADPCM command {Cmd:X2}",
                                            _sb.Dsp.Cmd);
                    }
                }
                break;

            case 0x20:
                DspAddData(0x7f); // Fake silent input for Creative parrot
                break;

            case 0x2c:
            case 0x98:
            case 0x99: // Documented only for DSP 2.x and 3.x
            case 0xa0:
            case 0xa8: // Documented only for DSP 3.x
                if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                    _loggerService.Error("DSP:Unimplemented input command {Cmd:X2}",
                                        _sb.Dsp.Cmd);
                }
                break;

            case 0xf9:
                // Sb16 ASP unknown function
                if (_sb.Type == SbType.Sb16) {
                    // Just feed it what it expects
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
                break;
        }

        _sb.Dsp.Cmd = 0;
        _sb.Dsp.CmdLen = 0;
        _sb.Dsp.In.Pos = 0;
    }

    private void DspChangeMode(DspMode mode) {
        if (_sb.Mode == mode) {
            return;
        }
        switch (mode) {
            case DspMode.Dac:
                _sb.Dac = new(_sb, _clock);
                break;
            case DspMode.None:
            case DspMode.Dma:
            case DspMode.DmaPause:
            case DspMode.DmaMasked:
                break;
        }
        _sb.Mode = mode;
    }

    private void DspChangeRate(uint freqHz) {
        if (_sb.FreqHz != freqHz && _sb.Dma.Mode != DmaMode.None) {
            int effectiveFreq = (int)(freqHz / (_sb.Mixer.StereoEnabled ? 2 : 1));
            SetChannelRateHz(effectiveFreq);

            _sb.Dma.Rate = (freqHz * _sb.Dma.Mul) >> SbShift;
            _sb.Dma.Min = (_sb.Dma.Rate * 3) / 1000;
        }
        _sb.FreqHz = freqHz;
    }

    /// <summary>
    /// Flushes any remaining DMA transfer that's shorter than the minimum threshold.
    /// This handles edge cases where the DMA transfer is so short it wouldn't be processed
    /// by the normal per-tick callback before the next one fires.
    /// </summary>
    private void FlushRemainingDmaTransfer() {
        if (_sb.Dma.Left == 0) {
            return;
        }
        if (!_sb.SpeakerEnabled && _sb.Type != SbType.Sb16) {
            uint numBytes = Math.Min(_sb.Dma.Min, _sb.Dma.Left);
            double delayMs = (numBytes * 1000.0) / _sb.Dma.Rate;
            _scheduler.AddEvent(SuppressDmaTransfer, delayMs, numBytes);
        } else if (_sb.Dma.Left < _sb.Dma.Min) {
            double delayMs = (_sb.Dma.Left * 1000.0) / _sb.Dma.Rate;
            _scheduler.AddEvent(ProcessDmaTransferEvent, delayMs, _sb.Dma.Left);
        }
    }

    private void ProcessDmaTransferEvent(uint bytesToProcess) {
        if (_sb.Dma.Left > 0) {
            uint toProcess = Math.Min(bytesToProcess, _sb.Dma.Left);
            PlayDmaTransfer(toProcess);
        }
    }

    private void SuppressDmaTransfer(uint bytesToRead) {
        uint numBytes = bytesToRead;
        if (_sb.Dma.Left < numBytes) {
            numBytes = _sb.Dma.Left;
        }
        uint read = ReadDma8Bit(numBytes);

        _sb.Dma.Left -= read;

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
            uint bigger = (_sb.Dma.Left > _sb.Dma.Min) ? _sb.Dma.Min : _sb.Dma.Left;
            double delayMs = (bigger * 1000.0) / _sb.Dma.Rate;
            _scheduler.AddEvent(SuppressDmaTransfer, delayMs, bigger);
        }
    }

    private void DspDoDmaTransfer(DmaMode mode, uint freqHz, bool autoInit, bool stereo) {
        // Starting a new transfer will clear any active irqs?
        _sb.Irq.Pending8Bit = false;
        _sb.Irq.Pending16Bit = false;
        _dualPic.DeactivateIrq(_sb.Hw.Irq);

        // Set up the multiplier based on DMA mode
        switch (mode) {
            case DmaMode.Adpcm2Bit:
                _sb.Dma.Mul = (1 << SbShift) / 4;
                break;
            case DmaMode.Adpcm3Bit:
                _sb.Dma.Mul = (1 << SbShift) / 3;
                break;
            case DmaMode.Adpcm4Bit:
                _sb.Dma.Mul = (1 << SbShift) / 2;
                break;
            case DmaMode.Pcm8Bit:
                _sb.Dma.Mul = 1 << SbShift;
                break;
            case DmaMode.Pcm16Bit:
                _sb.Dma.Mul = 1 << SbShift;
                break;
            case DmaMode.Pcm16BitAliased:
                _sb.Dma.Mul = (1 << SbShift) * 2;
                break;
            default:
                if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                    _loggerService.Error("SOUNDBLASTER: Illegal transfer mode {Mode}", mode);
                }
                return;
        }

        // Going from an active autoinit into a single cycle
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

        // Double the reading speed for stereo mode
        if (_sb.Dma.Stereo) {
            _sb.Dma.Mul *= 2;
        }

        // Calculate rate and minimum transfer size
        _sb.Dma.Rate = (_sb.FreqHz * _sb.Dma.Mul) >> SbShift;
        _sb.Dma.Min = (_sb.Dma.Rate * 3) / 1000;
        SetChannelRateHz((int)freqHz);
        _scheduler.RemoveEvents(ProcessDmaTransferEvent);
        _sb.Mode = DspMode.DmaMasked;
        _sb.Dma.Channel?.RegisterCallback(DspDmaCallback);
    }

    private void PerTickCallback() {
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
        if (_timingType == TimingType.PerTick) {
            double delay = TimeSpan.FromTicks(TimeSpan.TicksPerMillisecond).TotalMilliseconds;
            _scheduler.AddEvent(_perTickHandler, delay);
        }
    }

    private void PerFrameCallback() {
        if (!_dacChannel.IsEnabled) {
            SetCallbackNone();
            return;
        }
        int mixer_needs = Math.Max(System.Threading.Interlocked.Exchange(ref _framesNeeded, 0), 1);

        // Frames added this tick is only useful when we're in an underflow
        // situation with the mixer. generate_frames() may not give us
        // everything we need in a single call. We're not concerned about
        // over-filling while in this mode so just zero it out.
        _framesAddedThisTick = 0;
        while (_framesAddedThisTick < mixer_needs) {
            GenerateFrames(mixer_needs - _framesAddedThisTick);
        }

        AddNextFrameCallback();
    }

    private void AddNextFrameCallback() {
        double millisPerFrame = _dacChannel.MillisPerFrame;
        _scheduler.AddEvent(_perFrameHandler, millisPerFrame, 0);
    }

    private void SetCallbackNone() {
        if (_timingType != TimingType.None) {
            if (_timingType == TimingType.PerTick) {
                _scheduler.RemoveEvents(_perTickHandler);
            } else {
                _scheduler.RemoveEvents(_perFrameHandler);
            }

            _timingType = TimingType.None;
        }
    }

    private void SetCallbackPerTick() {
        if (_timingType != TimingType.PerTick) {
            SetCallbackNone();
            _framesAddedThisTick = 0;
            double delay = TimeSpan.FromTicks(TimeSpan.TicksPerMillisecond).TotalMilliseconds;
            _scheduler.AddEvent(_perTickHandler, delay);
            _timingType = TimingType.PerTick;
        }
    }

    private void SetCallbackPerFrame() {
        if (_timingType != TimingType.PerFrame) {
            SetCallbackNone();
            AddNextFrameCallback();
            _timingType = TimingType.PerFrame;
        }
    }

    private void GenerateFrames(int frames_requested) {
        if(!OutputQueue.IsRunning) {
            return;
        }
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
                    // This is a no-op if the channel is already running. DMA
                    // processing can go for some time using auto-init mode without
                    // having to send IO calls to the card; so we keep it awake when
                    // DMA is still running.
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

    private void DspPrepareDmaOld(DmaMode mode, bool autoInit, bool sign) {
        _sb.Dma.Sign = sign;

        // For single-cycle transfers, set up the size from the DSP input buffer
        if (!autoInit) {
            _sb.Dma.SingleSize = (uint)(1 + _sb.Dsp.In.Data[0] + (_sb.Dsp.In.Data[1] << 8));
        }

        // Always use 8-bit DMA channel for old-style commands
        _sb.Dma.Channel = _dmaBus.GetChannel(_sb.Hw.Dma8);
        DspDoDmaTransfer(mode,
            (uint)(_sb.FreqHz / (_sb.Mixer.StereoEnabled ? 2 : 1)),
            autoInit,
            _sb.Mixer.StereoEnabled);
    }

    /// <summary>
    /// Prepare DMA transfer for new-style (SB16) commands.
    /// </summary>
    private void DspPrepareDmaNew(DmaMode mode, uint length, bool autoInit, bool stereo) {
        uint freqHz = _sb.FreqHz;
        DmaMode newMode = mode;
        uint newLength = length;

        // Equal length if data format and dma channel are both 16-bit or 8-bit
        if (mode == DmaMode.Pcm16Bit) {
            if (_sb.Hw.Dma16 != 0xff) {
                _sb.Dma.Channel = _dmaBus.GetChannel(_sb.Hw.Dma16);
                if (_sb.Dma.Channel is null) {
                    _sb.Dma.Channel = _dmaBus.GetChannel(_sb.Hw.Dma8);
                    newMode = DmaMode.Pcm16BitAliased;
                    newLength *= 2;
                }
            } else {
                _sb.Dma.Channel = _dmaBus.GetChannel(_sb.Hw.Dma8);
                newMode = DmaMode.Pcm16BitAliased;
                // UNDOCUMENTED: In aliased mode sample length is written to DSP as
                // number of 16-bit samples so we need double 8-bit DMA buffer length
                newLength *= 2;
            }
        } else {
            _sb.Dma.Channel = _dmaBus.GetChannel(_sb.Hw.Dma8);
        }

        // Set the length to the correct register depending on mode
        if (autoInit) {
            _sb.Dma.AutoSize = newLength;
        } else {
            _sb.Dma.SingleSize = newLength;
        }
        DspDoDmaTransfer(newMode, freqHz, autoInit, stereo);
    }

    private void SetSpeakerEnabled(bool enabled) {
        // Speaker output is always enabled on the SB16 and ESS cards; speaker
        // enable/disable commands are simply ignored. Only the SB Pro and
        // earlier models can toggle the speaker-output.
        if (_sb.Type == SbType.Sb16 || _sb.EssType != EssType.None) {
            return;
        }
        if (_sb.SpeakerEnabled == enabled) {
            return;
        }
        // If the speaker's being turned on, then flush old
        // content before releasing the channel for playback.
        if (enabled) {
            _scheduler.RemoveEvents(SuppressDmaTransfer);
            FlushRemainingDmaTransfer();

            // Speaker powered-on after cold-state, give it warmup time
            _sb.Dsp.WarmupRemainingMs = _sb.Dsp.ColdWarmupMs;
        }

        _sb.SpeakerEnabled = enabled;
    }

    private bool ShouldUseHighDmaChannel() {
        return _sb.Type == SbType.Sb16 &&
               _config.HighDma >= 5 &&
               _config.HighDma != _config.LowDma;
    }

    private void InitSpeakerState() {
        if (_sb.Type == SbType.Sb16 || _sb.EssType != EssType.None) {
            // Speaker output (DAC output) is always enabled on the SB16 and
            // ESS cards. Because the channel is active, we treat this as a
            // startup event.
            bool isColdStart = _sb.Dsp.ResetTally <= DspInitialResetLimit;
            _sb.Dsp.WarmupRemainingMs = isColdStart ? _sb.Dsp.ColdWarmupMs : _sb.Dsp.HotWarmupMs;
            _sb.SpeakerEnabled = true;
        } else {
            // SB Pro and earlier models have the speaker-output disabled by default.
            _sb.SpeakerEnabled = false;
        }
    }

    private void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
        // Don't register any ports when Sound Blaster is disabled or has no base address
        if (_sb.Type == SbType.None || _config.BaseAddress == 0) {
            return;
        }
        // Register ports base+4 through base+0xF, skipping 8 and 9 (used by OPL).
        // SB1 and SB2 also skip ports 4 and 5 (mixer not present).
        int basePort = _config.BaseAddress;
        for (int i = 4; i <= 0xF; i++) {
            if (i is 8 or 9) {
                continue;
            }
            if ((_sb.Type == SbType.SB1 || _sb.Type == SbType.SB2) && (i == 4 || i == 5)) {
                continue;
            }
            ioPortDispatcher.AddIOPortHandler((ushort)(basePort + i), this);
        }
    }

    private void OnDmaChannelEvicted() {
        // Stop any active DMA transfer
        _sb.Mode = DspMode.None;
        _sb.Dma.Mode = DmaMode.None;
        _sb.Dma.Left = 0;
        _sb.Dma.Channel = null;

        // Clear pending IRQs
        _sb.Irq.Pending8Bit = false;
        _sb.Irq.Pending16Bit = false;
        _dualPic.DeactivateIrq(_sb.Hw.Irq);
    }

    /// <summary>
    /// Flushes the DSP output FIFO.
    /// </summary>
    private void DspFlushData() {
        _sb.Dsp.Out.Used = 0;
        _sb.Dsp.Out.Pos = 0;
    }

    /// <summary>
    /// Adds a byte to the DSP output FIFO (circular buffer of DspBufSize=64).
    /// </summary>
    private void DspAddData(byte value) {
        if (_sb.Dsp.Out.Used < DspBufSize) {
            int start = _sb.Dsp.Out.Used + _sb.Dsp.Out.Pos;
            if (start >= DspBufSize) {
                start -= DspBufSize;
            }
            _sb.Dsp.Out.Data[start] = value;
            _sb.Dsp.Out.Used++;
        } else {
            _loggerService.Error("SOUNDBLASTER: DSP output buffer full");
        }
    }

    /// <summary>
    /// Reads from the DSP output FIFO. Returns last value if empty (sticky).
    /// </summary>
    private byte DspReadData() {
        if (_sb.Dsp.Out.Used > 0) {
            _sb.Dsp.Out.LastVal = _sb.Dsp.Out.Data[_sb.Dsp.Out.Pos];
            _sb.Dsp.Out.Pos++;
            if (_sb.Dsp.Out.Pos >= DspBufSize) {
                _sb.Dsp.Out.Pos -= DspBufSize;
            }
            _sb.Dsp.Out.Used--;
        }
        return _sb.Dsp.Out.LastVal;
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
            case 0x00: // Reset
                CtmixerReset();
                break;

            case 0x02: // Master Volume (SB2 Only)
                WriteSbProVolume(_sb.Mixer.Master, (byte)((value & 0x0F) | (value << 4)));
                CtmixerUpdateVolumes();
                break;

            case 0x04: // DAC Volume (SBPRO)
                WriteSbProVolume(_sb.Mixer.Dac, value);
                CtmixerUpdateVolumes();
                break;

            case 0x06: { // FM output selection
                    WriteSbProVolume(_sb.Mixer.Fm, (byte)((value & 0x0F) | (value << 4)));
                    CtmixerUpdateVolumes();

                    if ((value & 0x60) != 0) {
                        if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                            _loggerService.Warning("Turned FM one channel off. not implemented {Value:X2}", value);
                        }
                    }
                }
                break;

            case 0x08: // CDA Volume (SB2 Only)
                WriteSbProVolume(_sb.Mixer.Cda, (byte)((value & 0x0F) | (value << 4)));
                CtmixerUpdateVolumes();
                break;

            case 0x0A: // Mic Level (SBPRO) or DAC Volume (SB2)
                if (_sb.Type == SbType.SB2) {
                    byte dacValue = (byte)(((value & 0x06) << 2) | 3);
                    _sb.Mixer.Dac[0] = dacValue;
                    _sb.Mixer.Dac[1] = dacValue;
                    CtmixerUpdateVolumes();
                } else {
                    _sb.Mixer.Mic = (byte)(((value & 0x07) << 2) | (_sb.Type == SbType.Sb16 ? 1 : 3));
                }
                break;

            case 0x0E: {
                    _sb.Mixer.StereoEnabled = (value & 0x02) != 0;

                    if (_sb.Type == SbType.SBPro2) {
                        bool lastFilterEnabled = _sb.Mixer.FilterEnabled;
                        _sb.Mixer.FilterEnabled = (value & 0x20) == 0;

                        if (_sb.Mixer.FilterConfigured && _sb.Mixer.FilterEnabled != lastFilterEnabled) {
                            if (_sb.Mixer.FilterAlwaysOn) {
                                if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                                    _loggerService.Debug("Filter always on; ignoring {Action} low-pass filter command",
                                        _sb.Mixer.FilterEnabled ? "enable" : "disable");
                                }
                            } else {
                                if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                                    _loggerService.Debug("{Action} low-pass filter",
                                        _sb.Mixer.FilterEnabled ? "Enabling" : "Disabling");
                                }
                                _dacChannel.LowPassFilter = _sb.Mixer.FilterEnabled ? FilterState.On : FilterState.Off;
                            }
                        }
                    }
                    DspChangeStereo(_sb.Mixer.StereoEnabled);
                    if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                        _loggerService.Warning("Mixer set to {Mode}", _sb.Dma.Stereo ? "STEREO" : "MONO");
                    }
                }
                break;

            case 0x14: // Audio 1 Play Volume (ESS)
                if (_sb.EssType != EssType.None) {
                    WriteEssVolume(value, _sb.Mixer.Dac);
                    CtmixerUpdateVolumes();
                }
                break;

            case 0x22: // Master Volume (SBPRO)
                WriteSbProVolume(_sb.Mixer.Master, value);
                CtmixerUpdateVolumes();
                break;

            case 0x26: // FM Volume (SBPRO)
                WriteSbProVolume(_sb.Mixer.Fm, value);
                CtmixerUpdateVolumes();
                break;

            case 0x28: // CD Audio Volume (SBPRO)
                WriteSbProVolume(_sb.Mixer.Cda, value);
                CtmixerUpdateVolumes();
                break;

            case 0x2E: // Line-in Volume (SBPRO)
                WriteSbProVolume(_sb.Mixer.Lin, value);
                break;

            case 0x30: // Master Volume Left (SB16)
                if (_sb.Type == SbType.Sb16) {
                    _sb.Mixer.Master[0] = (byte)(value >> 3);
                    CtmixerUpdateVolumes();
                }
                break;

            case 0x31: // Master Volume Right (SB16)
                if (_sb.Type == SbType.Sb16) {
                    _sb.Mixer.Master[1] = (byte)(value >> 3);
                    CtmixerUpdateVolumes();
                }
                break;

            case 0x32:
                if (_sb.Type == SbType.Sb16) {
                    _sb.Mixer.Dac[0] = (byte)(value >> 3);
                    CtmixerUpdateVolumes();
                } else if (_sb.EssType != EssType.None) {
                    WriteEssVolume(value, _sb.Mixer.Master);
                    CtmixerUpdateVolumes();
                }
                break;

            case 0x33: // DAC Volume Right (SB16)
                if (_sb.Type == SbType.Sb16) {
                    _sb.Mixer.Dac[1] = (byte)(value >> 3);
                    CtmixerUpdateVolumes();
                }
                break;

            case 0x34: // FM Volume Left (SB16)
                if (_sb.Type == SbType.Sb16) {
                    _sb.Mixer.Fm[0] = (byte)(value >> 3);
                    CtmixerUpdateVolumes();
                }
                break;

            case 0x35: // FM Volume Right (SB16)
                if (_sb.Type == SbType.Sb16) {
                    _sb.Mixer.Fm[1] = (byte)(value >> 3);
                    CtmixerUpdateVolumes();
                }
                break;

            case 0x36:
                if (_sb.Type == SbType.Sb16) {
                    _sb.Mixer.Cda[0] = (byte)(value >> 3);
                    CtmixerUpdateVolumes();
                } else if (_sb.EssType != EssType.None) {
                    WriteEssVolume(value, _sb.Mixer.Fm);
                    CtmixerUpdateVolumes();
                }
                break;

            case 0x37: // CD Volume Right (SB16)
                if (_sb.Type == SbType.Sb16) {
                    _sb.Mixer.Cda[1] = (byte)(value >> 3);
                    CtmixerUpdateVolumes();
                }
                break;

            case 0x38:
                if (_sb.Type == SbType.Sb16) {
                    _sb.Mixer.Lin[0] = (byte)(value >> 3);
                } else if (_sb.EssType != EssType.None) {
                    WriteEssVolume(value, _sb.Mixer.Cda);
                    CtmixerUpdateVolumes();
                }
                break;

            case 0x39: // Line-in Volume Right (SB16)
                if (_sb.Type == SbType.Sb16) {
                    _sb.Mixer.Lin[1] = (byte)(value >> 3);
                }
                break;

            case 0x3A: // Mic Volume (SB16)
                if (_sb.Type == SbType.Sb16) {
                    _sb.Mixer.Mic = (byte)(value >> 3);
                }
                break;

            case 0x3E: // Line Volume (ESS)
                if (_sb.EssType != EssType.None) {
                    WriteEssVolume(value, _sb.Mixer.Lin);
                }
                break;

            case 0x80: // IRQ Select
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

            case 0x81: // DMA Select
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
            case 0x00: // Reset
                return 0x00;

            case 0x02: // Master Volume (SB2 only)
                return (byte)((_sb.Mixer.Master[1] >> 1) & 0x0E);

            case 0x14: // Audio 1 Play Volume (ESS)
                if (_sb.EssType != EssType.None) {
                    ret = ReadEssVolume(_sb.Mixer.Dac);
                }
                break;

            case 0x22: // Master Volume (SB Pro)
                return ReadSbProVolume(_sb.Mixer.Master);

            case 0x04: // DAC Volume (SB Pro)
                return ReadSbProVolume(_sb.Mixer.Dac);

            case 0x06: // FM Volume (SB2 only) + FM output selection
                return (byte)((_sb.Mixer.Fm[1] >> 1) & 0x0E);

            case 0x08: // CD Volume (SB2 only)
                return (byte)((_sb.Mixer.Cda[1] >> 1) & 0x0E);

            case 0x0A: // Mic Level (SB Pro) or Voice (SB2 only)
                if (_sb.Type == SbType.SB2) {
                    return (byte)(_sb.Mixer.Dac[0] >> 2);
                }
                return (byte)((_sb.Mixer.Mic >> 2) & (_sb.Type == SbType.Sb16 ? 7 : 6));

            case 0x0E: // Output/Stereo Select
                return (byte)(0x11 | (_sb.Mixer.StereoEnabled ? 0x02 : 0x00) | (_sb.Mixer.FilterEnabled ? 0x00 : 0x20));

            case 0x26: // FM Volume (SB Pro)
                return ReadSbProVolume(_sb.Mixer.Fm);

            case 0x28: // CD Audio Volume (SB Pro)
                return ReadSbProVolume(_sb.Mixer.Cda);

            case 0x2E: // Line-in Volume (SB Pro)
                return ReadSbProVolume(_sb.Mixer.Lin);

            case 0x30: // Master Volume Left (SB16)
                if (_sb.Type == SbType.Sb16) {
                    return (byte)(_sb.Mixer.Master[0] << 3);
                }
                ret = 0x0A;
                break;

            case 0x31: // Master Volume Right (SB16)
                if (_sb.Type == SbType.Sb16) {
                    return (byte)(_sb.Mixer.Master[1] << 3);
                }
                ret = 0x0A;
                break;

            case 0x32:
                if (_sb.Type == SbType.Sb16) {
                    return (byte)(_sb.Mixer.Dac[0] << 3);
                }
                if (_sb.EssType != EssType.None) {
                    return ReadEssVolume(_sb.Mixer.Master);
                }
                ret = 0x0A;
                break;

            case 0x33: // DAC Volume Right (SB16)
                if (_sb.Type == SbType.Sb16) {
                    return (byte)(_sb.Mixer.Dac[1] << 3);
                }
                ret = 0x0A;
                break;

            case 0x34: // FM Volume Left (SB16)
                if (_sb.Type == SbType.Sb16) {
                    return (byte)(_sb.Mixer.Fm[0] << 3);
                }
                ret = 0x0A;
                break;

            case 0x35: // FM Volume Right (SB16)
                if (_sb.Type == SbType.Sb16) {
                    return (byte)(_sb.Mixer.Fm[1] << 3);
                }
                ret = 0x0A;
                break;

            case 0x36:
                if (_sb.Type == SbType.Sb16) {
                    return (byte)(_sb.Mixer.Cda[0] << 3);
                }
                if (_sb.EssType != EssType.None) {
                    return ReadEssVolume(_sb.Mixer.Fm);
                }
                ret = 0x0A;
                break;

            case 0x37: // CD Volume Right (SB16)
                if (_sb.Type == SbType.Sb16) {
                    return (byte)(_sb.Mixer.Cda[1] << 3);
                }
                ret = 0x0A;
                break;

            case 0x38:
                if (_sb.Type == SbType.Sb16) {
                    return (byte)(_sb.Mixer.Lin[0] << 3);
                }
                if (_sb.EssType != EssType.None) {
                    return ReadEssVolume(_sb.Mixer.Cda);
                }
                ret = 0x0A;
                break;

            case 0x39: // Line-in Volume Right (SB16)
                if (_sb.Type == SbType.Sb16) {
                    return (byte)(_sb.Mixer.Lin[1] << 3);
                }
                ret = 0x0A;
                break;

            case 0x3A: // Mic Volume (SB16)
                if (_sb.Type == SbType.Sb16) {
                    return (byte)(_sb.Mixer.Mic << 3);
                }
                ret = 0x0A;
                break;

            case 0x3E: // Line Volume (ESS)
                if (_sb.EssType != EssType.None) {
                    ret = ReadEssVolume(_sb.Mixer.Lin);
                }
                break;

            case 0x40: // ESS Identification Value (ES1488 and later)
                if (_sb.EssType is EssType.Es1688 or EssType.None) {
                    ret = _sb.Mixer.EssIdStr[_sb.Mixer.EssIdStrPos];
                    _sb.Mixer.EssIdStrPos++;
                    if (_sb.Mixer.EssIdStrPos >= 4) {
                        _sb.Mixer.EssIdStrPos = 0;
                    }
                } else {
                    ret = 0x0A;
                }
                break;

            case 0x80: // IRQ Select
                ret = 0;
                return _sb.Hw.Irq switch {
                    2 => 0x01,
                    5 => 0x02,
                    7 => 0x04,
                    10 => 0x08,
                    _ => ret,
                };
            case 0x81: // DMA Select
                ret = 0;
                switch (_sb.Hw.Dma8) {
                    case 0: ret |= 0x01; break;
                    case 1: ret |= 0x02; break;
                    case 3: ret |= 0x08; break;
                }
                switch (_sb.Hw.Dma16) {
                    case 5: ret |= 0x20; break;
                    case 6: ret |= 0x40; break;
                    case 7: ret |= 0x80; break;
                }
                return ret;

            case 0x82: // IRQ Status
                return (byte)((_sb.Irq.Pending8Bit ? 0x01 : 0x00) |
                              (_sb.Irq.Pending16Bit ? 0x02 : 0x00) |
                              (_sb.Type == SbType.Sb16 ? 0x20 : 0x00));

            default:
                if (((_sb.Type == SbType.SBPro1 || _sb.Type == SbType.SBPro2) &&
                     _sb.Mixer.Index == 0x0C) ||
                    (_sb.Type == SbType.Sb16 && _sb.Mixer.Index >= 0x3B && _sb.Mixer.Index <= 0x47)) {
                    ret = _sb.Mixer.Unhandled[_sb.Mixer.Index];
                } else {
                    ret = 0x0A;
                }
                break;
        }
        return ret;
    }

    /// <summary>
    /// Determines if the DSP write buffer is at capacity.
    /// </summary>
    private bool WriteBufferAtCapacity() {
        // Is the DSP in an abnormal state?
        if (_sb.Dsp.State != DspState.Normal) {
            return true;
        }

        // Report the buffer as having some room every 8th call
        if ((++_sb.Dsp.WriteStatusCounter % 8) == 0) {
            return false;
        }

        // If DMA isn't running then the buffer's definitely not at capacity
        if (_sb.Dma.Mode == DmaMode.None) {
            return false;
        }

        // The DMA buffer is considered full until it can accept a full write
        return _sb.Dma.Left > _sb.Dma.Min;
    }
}
