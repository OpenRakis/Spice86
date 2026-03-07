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
using System.Runtime.InteropServices;

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

        _toFloatUnsigned8 = (s, i) => ToFloat(s[i]);
        _toFloatSigned8 = (s, i) => ToFloat(s[i]);
        _toFloatUnsigned16 = (s, i) => ToFloat(s[i]);
        _toFloatSigned16 = (s, i) => ToFloat(s[i]);

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
            case (byte)SoundBlasterPortOffset.MixerIndex:
                result = _sb.Mixer.Index;
                return result;

            case (byte)SoundBlasterPortOffset.MixerData:
                result = CtmixerRead();
                return result;

            case (byte)SoundBlasterPortOffset.DspReadData:
                result = DspReadData();
                return result;

            case (byte)SoundBlasterPortOffset.DspWriteStatus: {
                    // Bit 7 = 1 means buffer at capacity (not ready to receive).
                    // Lower 7 bits are always 1.
                    byte writeStatus = 0x7F; // lower 7 bits always set
                    if (WriteBufferAtCapacity()) {
                        writeStatus |= 0x80;
                    }
                    return writeStatus;
                }

            case (byte)SoundBlasterPortOffset.DspReadStatus: {
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

            case (byte)SoundBlasterPortOffset.DspAck16Bit:
                _sb.Irq.Pending16Bit = false;
                return 0xFF;

            case (byte)SoundBlasterPortOffset.DspReset:
                return 0xFF;

            default:
                return 0xFF;
        }
    }

    public override void WriteByte(ushort port, byte value) {
        int offset = port - _config.BaseAddress;
        switch (offset) {
            case (byte)SoundBlasterPortOffset.DspReset:
                DspDoReset(value);
                break;
            case (byte)SoundBlasterPortOffset.DspWriteData:
                DspDoWrite(value);
                break;
            case (byte)SoundBlasterPortOffset.MixerIndex:
                _sb.Mixer.Index = value;
                break;
            case (byte)SoundBlasterPortOffset.MixerData:
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
                            ReadOnlySpan<sbyte> signedBuf = MemoryMarshal.Cast<byte, sbyte>(_sb.Dma.Buf8.AsSpan());
                            EnqueueFrames(MaybeSilence(signedBuf, samples, FrameType.Stereo));
                        } else {
                            EnqueueFrames(MaybeSilence(_sb.Dma.Buf8, samples, FrameType.Stereo));
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
                        ReadOnlySpan<sbyte> signedBuf = MemoryMarshal.Cast<byte, sbyte>(_sb.Dma.Buf8.AsSpan());
                        EnqueueFrames(MaybeSilence(signedBuf, samples, FrameType.Mono));
                    } else {
                        EnqueueFrames(MaybeSilence(_sb.Dma.Buf8, samples, FrameType.Mono));
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
                            EnqueueFrames(MaybeSilence(_sb.Dma.Buf16, samples, FrameType.Stereo));
                        } else {
                            ReadOnlySpan<ushort> unsignedBuf = MemoryMarshal.Cast<short, ushort>(_sb.Dma.Buf16.AsSpan());
                            EnqueueFrames(MaybeSilence(unsignedBuf, samples, FrameType.Stereo));
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
                        EnqueueFrames(MaybeSilence(_sb.Dma.Buf16, samples, FrameType.Mono));
                    } else {
                        ReadOnlySpan<ushort> unsignedBuf = MemoryMarshal.Cast<short, ushort>(_sb.Dma.Buf16.AsSpan());
                        EnqueueFrames(MaybeSilence(unsignedBuf, samples, FrameType.Mono));
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
            _scheduler.RemoveEvents(PlayDmaTransfer);

            if (_sb.Dma.Mode >= DmaMode.Pcm16Bit) {
                RaiseIrq(SbIrq.Irq16);
            } else {
                RaiseIrq(SbIrq.Irq8);
            }

            if (!_sb.Dma.AutoInit) {
                // Not new single cycle transfer waiting?
                if (_sb.Dma.SingleSize == 0) {
                    _sb.Mode = DspMode.None;
                    _sb.Dma.Mode = DmaMode.None;
                } else {
                    // A single size transfer is still waiting,
                    // handle that now
                    _sb.Dma.Left = _sb.Dma.SingleSize;
                    _sb.Dma.SingleSize = 0;
                }
            } else {
                if (_sb.Dma.AutoSize == 0) {
                    _sb.Mode = DspMode.None;
                }
                // Continue with a new auto init transfer
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
            EnqueueFrames(MaybeSilence(decoded, numDecoded, FrameType.Mono));
            numSamples += numDecoded;
            i++;
        }

        // ADPCM is mono
        ushort numFrames = (ushort)numSamples;
        return (numBytes, numSamples, numFrames);
    }

    private void EnqueueFrames(Span<AudioFrame> frames) {
        if (frames.Length == 0) {
            return;
        }

        _framesAddedThisTick += frames.Length;
        _outputQueue.NonblockingBulkEnqueue(frames);
    }

    private void EnqueueSilentFrames(uint count) {
        _enqueueBatchCount = 0;
        AddSilentFramesToBatch((int)count);
        FlushEnqueueBatch();
    }

    private void FlushEnqueueBatch() {
        if (_enqueueBatchCount == 0) {
            return;
        }
        EnqueueFrames(_enqueueBatch.AsSpan(0, _enqueueBatchCount));
        _enqueueBatchCount = 0;
    }

    private void AddSilentFramesToBatch(int numFrames) {
        AudioFrame silence = new AudioFrame(0.0f, 0.0f);
        for (int i = 0; i < numFrames; i++) {
            _enqueueBatch[_enqueueBatchCount++] = silence;
        }
    }

    private static float ToFloat(byte sample) {
        return LookupTables.ToUnsigned8(sample);
    }

    private static float ToFloat(sbyte sample) {
        return LookupTables.ToSigned8(sample);
    }

    private static float ToFloat(ushort sample) {
        return LookupTables.ToUnsigned16(sample);
    }

    private static float ToFloat(short sample) {
        return LookupTables.ToSigned16(sample);
    }

    private Span<AudioFrame> MaybeSilence<T>(ReadOnlySpan<T> samples, uint numSamples, FrameType frameType) where T : unmanaged {
        int samplesPerFrame;
        if (frameType == FrameType.Mono) {
            samplesPerFrame = 1;
        } else {
            samplesPerFrame = 2;
        }
        int numFrames = (int)(numSamples / (uint)samplesPerFrame);

        if (numFrames <= 0) {
            return [];
        }

        _enqueueBatchCount = 0;

        if (_sb.Dsp.WarmupRemainingMs > 0) {
            _sb.Dsp.WarmupRemainingMs--;
            AddSilentFramesToBatch(numFrames);
            _framesAddedThisTick += numFrames;
            return _enqueueBatch.AsSpan(0, _enqueueBatchCount);
        }

        if (!_sb.SpeakerEnabled) {
            AddSilentFramesToBatch(numFrames);
            _framesAddedThisTick += numFrames;
            return _enqueueBatch.AsSpan(0, _enqueueBatchCount);
        }

        BuildFrames(samples, numFrames, samplesPerFrame, frameType);
        _framesAddedThisTick += numFrames;
        return _enqueueBatch.AsSpan(0, _enqueueBatchCount);
    }

    private void BuildFrames<T>(ReadOnlySpan<T> samples, int numFrames, int samplesPerFrame, FrameType frameType) where T : unmanaged {
        bool isUnsigned8 = typeof(T) == typeof(byte);
        bool isSigned8 = typeof(T) == typeof(sbyte);
        bool isUnsigned16 = typeof(T) == typeof(ushort);
        bool isSigned16 = typeof(T) == typeof(short);

        if (isUnsigned8) {
            BuildFramesUnsigned8(MemoryMarshal.Cast<T, byte>(samples), numFrames, samplesPerFrame, frameType);
        } else if (isSigned8) {
            BuildFramesSigned8(MemoryMarshal.Cast<T, sbyte>(samples), numFrames, samplesPerFrame, frameType);
        } else if (isUnsigned16) {
            BuildFramesUnsigned16(MemoryMarshal.Cast<T, ushort>(samples), numFrames, samplesPerFrame, frameType);
        } else if (isSigned16) {
            BuildFramesSigned16(MemoryMarshal.Cast<T, short>(samples), numFrames, samplesPerFrame, frameType);
        }
    }

    private void BuildFramesUnsigned8(ReadOnlySpan<byte> samples, int numFrames, int samplesPerFrame, FrameType frameType) {
        BuildFramesShared(samples, numFrames, samplesPerFrame, frameType, _toFloatUnsigned8);
    }

    private void BuildFramesSigned8(ReadOnlySpan<sbyte> samples, int numFrames, int samplesPerFrame, FrameType frameType) {
        BuildFramesShared(samples, numFrames, samplesPerFrame, frameType, _toFloatSigned8);
    }

    private void BuildFramesUnsigned16(ReadOnlySpan<ushort> samples, int numFrames, int samplesPerFrame, FrameType frameType) {
        BuildFramesShared(samples, numFrames, samplesPerFrame, frameType, _toFloatUnsigned16);
    }

    private void BuildFramesSigned16(ReadOnlySpan<short> samples, int numFrames, int samplesPerFrame, FrameType frameType) {
        BuildFramesShared(samples, numFrames, samplesPerFrame, frameType, _toFloatSigned16);
    }

    private void BuildFramesShared<TSample>(ReadOnlySpan<TSample> samples, int numFrames, int samplesPerFrame, FrameType frameType, Func<ReadOnlySpan<TSample>, int, float> getSample) where TSample : unmanaged {
        for (int i = 0; i < numFrames; ++i) {
            int leftIndex = i * samplesPerFrame;
            float left = getSample(samples, leftIndex);
            float right;
            if (frameType == FrameType.Mono) {
                right = left;
            } else {
                right = getSample(samples, i * 2 + 1);
            }

            if (_sb.Type == SbType.SBPro1 || _sb.Type == SbType.SBPro2) {
                _enqueueBatch[_enqueueBatchCount++] = new AudioFrame(right, left);
            } else {
                _enqueueBatch[_enqueueBatchCount++] = new AudioFrame(left, right);
            }
        }
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
        _scheduler.RemoveEvents(PlayDmaTransfer);
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
            case (byte)DspCommand.DspStatusOrAspSetMode:
                if (_sb.Type == SbType.Sb16) {
                    if ((_sb.Dsp.In.Data[0] & 0xf1) == 0xf1) {
                        _aspInitInProgress = true;
                    } else {
                        _aspInitInProgress = false;
                    }
                } else {
                    DspFlushData();
                    if (_sb.Type == SbType.SB2) {
                        DspAddData(0x88);
                    } else if (_sb.Type is SbType.SBPro1 or SbType.SBPro2) {
                        DspAddData(0x7b);
                    } else {
                        DspAddData(0xff);
                    }
                }
                break;

            case (byte)DspCommand.AspSetCodecParameter:
                break;

            case (byte)DspCommand.AspGetVersion:
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

            case (byte)DspCommand.AspSetRegister:
                if (_sb.Type == SbType.Sb16) {
                    _aspRegs[_sb.Dsp.In.Data[0]] = _sb.Dsp.In.Data[1];
                }
                break;

            case (byte)DspCommand.AspGetRegister:
                if (_sb.Type == SbType.Sb16) {
                    if (_aspInitInProgress && (_sb.Dsp.In.Data[0] == 0x83)) {
                        _aspRegs[0x83] = (byte)~_aspRegs[0x83];
                    }
                    DspAddData(_aspRegs[_sb.Dsp.In.Data[0]]);
                }
                break;

            case (byte)DspCommand.DirectDac:
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

            case (byte)DspCommand.SingleCycle8BitDmaAdc:
                _sb.Dma.Left = (uint)(1 + _sb.Dsp.In.Data[0] + (_sb.Dsp.In.Data[1] << 8));
                _sb.Dma.Sign = false;
                _dmaBus.GetChannel(_sb.Hw.Dma8)?.RegisterCallback(DspAdcCallback);
                break;

            case (byte)DspCommand.SingleCycle8BitDmaDac:
                goto case (byte)DspCommand.SingleCycle8BitDmaDacWari;

            case (byte)DspCommand.SingleCycle8BitDmaDacWari:
                goto case (byte)DspCommand.SingleCycle8BitDmaHighSpeed;

            case (byte)DspCommand.SingleCycle8BitDmaHighSpeed:
                DspPrepareDmaOld(DmaMode.Pcm8Bit, false, false);
                break;

            case (byte)DspCommand.AutoInit8BitDma:
            case (byte)DspCommand.AutoInit8BitDmaHighSpeed:
                if (_sb.Type > SbType.SB1) {
                    DspPrepareDmaOld(DmaMode.Pcm8Bit, true, false);
                }
                break;

            case (byte)DspCommand.WriteMidiOutput:
                if (_sb.MidiEnabled) {
                    // TODO: Forward to MIDI subsystem
                }
                break;

            case (byte)DspCommand.SetTimeConstant:
                DspChangeRate((uint)(1000000 / (256 - _sb.Dsp.In.Data[0])));
                break;

            case (byte)DspCommand.SetOutputSampleRate:
            case (byte)DspCommand.SetInputSampleRate:
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

            case (byte)DspCommand.SetDmaBlockSize:
                if (_sb.Type > SbType.SB1) {
                    _sb.Dma.AutoSize = (uint)(1 + _sb.Dsp.In.Data[0] + (_sb.Dsp.In.Data[1] << 8));
                }
                break;

            case (byte)DspCommand.SingleCycleAdpcm4BitRef:
                _sb.Adpcm.HaveRef = true;
                goto case (byte)DspCommand.SingleCycleAdpcm4Bit;

            case (byte)DspCommand.SingleCycleAdpcm4Bit:
                DspPrepareDmaOld(DmaMode.Adpcm4Bit, false, false);
                break;

            case (byte)DspCommand.SingleCycleAdpcm3BitRef:
                _sb.Adpcm.HaveRef = true;
                goto case (byte)DspCommand.SingleCycleAdpcm3Bit;

            case (byte)DspCommand.SingleCycleAdpcm3Bit:
                DspPrepareDmaOld(DmaMode.Adpcm3Bit, false, false);
                break;

            case (byte)DspCommand.AutoInitAdpcm4BitRef:
                if (_sb.Type > SbType.SB1) {
                    _sb.Adpcm.HaveRef = true;
                    DspPrepareDmaOld(DmaMode.Adpcm4Bit, true, false);
                }
                break;

            case (byte)DspCommand.SingleCycleAdpcm2BitRef:
                _sb.Adpcm.HaveRef = true;
                goto case (byte)DspCommand.SingleCycleAdpcm2Bit;

            case (byte)DspCommand.SingleCycleAdpcm2Bit:
                DspPrepareDmaOld(DmaMode.Adpcm2Bit, false, false);
                break;

            case (byte)DspCommand.SilenceDac: {
                    uint samples = (uint)(1 + _sb.Dsp.In.Data[0] + (_sb.Dsp.In.Data[1] << 8));
                    double delayMs = (1000.0 * samples) / _sb.FreqHz;
                    _scheduler.AddEvent(DspRaiseIrqEvent, delayMs, 0);
                }
                break;

            case (byte)DspCommand.Generic8BitDmaB0:
            case (byte)DspCommand.Generic8BitDmaB1:
            case (byte)DspCommand.Generic8BitDmaB2:
            case (byte)DspCommand.Generic8BitDmaB3:
            case (byte)DspCommand.Generic8BitDmaB4:
            case (byte)DspCommand.Generic8BitDmaB5:
            case (byte)DspCommand.Generic8BitDmaB6:
            case (byte)DspCommand.Generic8BitDmaB7:
            case (byte)DspCommand.Generic8BitDmaB8:
            case (byte)DspCommand.Generic8BitDmaB9:
            case (byte)DspCommand.Generic8BitDmaBA:
            case (byte)DspCommand.Generic8BitDmaBB:
            case (byte)DspCommand.Generic8BitDmaBC:
            case (byte)DspCommand.Generic8BitDmaBD:
            case (byte)DspCommand.Generic8BitDmaBE:
            case (byte)DspCommand.Generic8BitDmaBF:
            case (byte)DspCommand.Generic16BitDmaC0:
            case (byte)DspCommand.Generic16BitDmaC1:
            case (byte)DspCommand.Generic16BitDmaC2:
            case (byte)DspCommand.Generic16BitDmaC3:
            case (byte)DspCommand.Generic16BitDmaC4:
            case (byte)DspCommand.Generic16BitDmaC5:
            case (byte)DspCommand.Generic16BitDmaC6:
            case (byte)DspCommand.Generic16BitDmaC7:
            case (byte)DspCommand.Generic16BitDmaC8:
            case (byte)DspCommand.Generic16BitDmaC9:
            case (byte)DspCommand.Generic16BitDmaCA:
            case (byte)DspCommand.Generic16BitDmaCB:
            case (byte)DspCommand.Generic16BitDmaCC:
            case (byte)DspCommand.Generic16BitDmaCD:
            case (byte)DspCommand.Generic16BitDmaCE:
            case (byte)DspCommand.Generic16BitDmaCF:
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

            case (byte)DspCommand.Halt16BitDma:
                if (_sb.Type == SbType.Sb16) {
                    goto case (byte)DspCommand.Halt8BitDma;
                }
                break;

            case (byte)DspCommand.Halt8BitDma:
                _sb.Mode = DspMode.DmaPause;
                _scheduler.RemoveEvents(PlayDmaTransfer);
                break;

            case (byte)DspCommand.EnableSpeaker:
                SetSpeakerEnabled(true);
                break;

            case (byte)DspCommand.DisableSpeaker:
                SetSpeakerEnabled(false);
                break;

            case (byte)DspCommand.GetSpeakerStatus:
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

            case (byte)DspCommand.Continue16BitDma:
                if (_sb.Type != SbType.Sb16) {
                    break;
                }
                goto case (byte)DspCommand.Continue8BitDma;

            case (byte)DspCommand.Continue8BitDma:
                if (_sb.Mode == DspMode.DmaPause) {
                    _sb.Mode = DspMode.DmaMasked;
                    _sb.Dma.Channel?.RegisterCallback(DspDmaCallback);
                }
                break;

            case (byte)DspCommand.ExitAutoInit16Bit:
                if (_sb.Type == SbType.Sb16) {
                    goto case (byte)DspCommand.ExitAutoInit8Bit;
                }
                break;

            case (byte)DspCommand.ExitAutoInit8Bit:
                if (_sb.Type > SbType.SB1) {
                    _sb.Dma.AutoInit = false;
                }
                break;

            case (byte)DspCommand.DspIdentification:
                DspFlushData();
                DspAddData((byte)~_sb.Dsp.In.Data[0]);
                break;

            case (byte)DspCommand.GetDspVersion:
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

            case (byte)DspCommand.DmaIdentification:
                for (int i = 0; i < 8; i++) {
                    if (((_sb.Dsp.In.Data[0] >> i) & 0x01) != 0) {
                        _sb.E2.Value += E2IncrTable[_sb.E2.Count % 4][i];
                    }
                }
                _sb.E2.Value += E2IncrTable[_sb.E2.Count % 4][8];
                _sb.E2.Count++;
                _dmaBus.GetChannel(_sb.Hw.Dma8)?.RegisterCallback(DspE2DmaCallback);
                break;

            case (byte)DspCommand.GetDspCopyright:
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

            case (byte)DspCommand.WriteTestRegister:
                _sb.Dsp.TestRegister = _sb.Dsp.In.Data[0];
                break;

            case (byte)DspCommand.EssDetectReadConfig:
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

            case (byte)DspCommand.ReadTestRegister:
                DspFlushData();
                DspAddData(_sb.Dsp.TestRegister);
                break;

            case (byte)DspCommand.Trigger8BitIrq:
                // Small delay to emulate DSP slowness, fixes Llamatron 2012 and Lemmings 3D
                _scheduler.AddEvent(DspRaiseIrqEvent, 0.01, 0);
                break;

            case (byte)DspCommand.Trigger16BitIrq:
                if (_sb.Type == SbType.Sb16) {
                    RaiseIrq(SbIrq.Irq16);
                }
                break;

            case (byte)DspCommand.UndocumentedF8:
                DspFlushData();
                DspAddData(0);
                break;

            case (byte)DspCommand.UnimplementedMidiIo30:
            case (byte)DspCommand.UnimplementedMidiIo31:
                if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                    _loggerService.Error("DSP:Unimplemented MIDI I/O command {Cmd:X2}",
                                        _sb.Dsp.Cmd);
                }
                break;

            case (byte)DspCommand.UnimplementedMidiUart34:
            case (byte)DspCommand.UnimplementedMidiUart35:
            case (byte)DspCommand.UnimplementedMidiUart36:
            case (byte)DspCommand.UnimplementedMidiUart37:
                if (_sb.Type > SbType.SB1) {
                    if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                        _loggerService.Error("DSP:Unimplemented MIDI UART command {Cmd:X2}",
                                            _sb.Dsp.Cmd);
                    }
                }
                break;

            case (byte)DspCommand.UnimplementedAutoInitAdpcm7F:
            case (byte)DspCommand.UnimplementedAutoInitAdpcm1F:
                if (_sb.Type > SbType.SB1) {
                    if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                        _loggerService.Error("DSP:Unimplemented auto-init DMA ADPCM command {Cmd:X2}",
                                            _sb.Dsp.Cmd);
                    }
                }
                break;

            case (byte)DspCommand.CreativeParrotInput:
                DspAddData(0x7f);
                break;

            case (byte)DspCommand.UnimplementedInput2C:
            case (byte)DspCommand.UnimplementedInput98:
            case (byte)DspCommand.UnimplementedInput99:
            case (byte)DspCommand.UnimplementedInputA0:
            case (byte)DspCommand.UnimplementedInputA8:
                if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                    _loggerService.Error("DSP:Unimplemented input command {Cmd:X2}",
                                        _sb.Dsp.Cmd);
                }
                break;

            case (byte)DspCommand.AspUnknownFunction:
                if (_sb.Type == SbType.Sb16) {
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
            _scheduler.AddEvent(PlayDmaTransfer, delayMs, _sb.Dma.Left);
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
        _scheduler.RemoveEvents(PlayDmaTransfer);
        _sb.Mode = DspMode.DmaMasked;
        _sb.Dma.Channel?.RegisterCallback(DspDmaCallback);
    }

    private void PerTickCallback() {
        if (!_dacChannel.IsEnabled) {
            SetCallbackNone();
            return;
        }
        _frameCounter += MathF.Max(
            Interlocked.Exchange(ref _framesNeeded, 0),
            _dacChannel.FramesPerTick);

        int totalFrames = (int)MathF.Floor(_frameCounter);
        _frameCounter -= totalFrames;

        while (_framesAddedThisTick < totalFrames) {
            GenerateFrames(totalFrames - _framesAddedThisTick);
        }

        _framesAddedThisTick -= totalFrames;
        if (_timingType == TimingType.PerTick) {
            AddPerTickCallback();
        }
    }

    private void AddPerTickCallback() {
        // 1 (old value): too much delays, underruns in Dune voices and sound effects
        // 0.1: way too fast with Dune voices, lips moved at light speed
        // 0.5: Good value for both Krondor and Dune
        // 0.6: Good value, tested with Krondor, Dune, Prince of Persia, and Wolf3D
        const double delay = 0.6;
        _scheduler.AddEvent(_perTickHandler, delay);
    }

    private void PerFrameCallback() {
        if (!_dacChannel.IsEnabled) {
            SetCallbackNone();
            return;
        }
        int mixerNeeds = Math.Max(Interlocked.Exchange(ref _framesNeeded, 0), 1);

        // Frames added this tick is only useful when we're in an underflow
        // situation with the mixer. GenerateFrames() may not give us
        // everything we need in a single call. We're not concerned about
        // over-filling while in this mode so just zero it out.
        _framesAddedThisTick = 0;
        while (_framesAddedThisTick < mixerNeeds) {
            GenerateFrames(mixerNeeds - _framesAddedThisTick);
        }

        AddNextFrameCallback();
    }

    private void AddNextFrameCallback() {
        double delay = _dacChannel.MillisPerFrame;
        _scheduler.AddEvent(_perFrameHandler, delay, 0);
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
            AddPerTickCallback();
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
        if (!OutputQueue.IsRunning) {
            return;
        }
        switch (_sb.Mode) {
            case DspMode.None:
            case DspMode.DmaPause:
            case DspMode.DmaMasked: {
                    if (_emptyFrames.Length < frames_requested) {
                        _emptyFrames = new AudioFrame[frames_requested];
                    }
                    EnqueueFrames(_emptyFrames.AsSpan(0, frames_requested));
                    break;
                }

            case DspMode.Dac:
                // DAC mode typically renders one frame at a time because the
                // DOS program will be writing to the DAC register at the
                // playback rate. In a mixer underflow situation, we render the
                // current frame multiple times.
                for (int i = 0; i < frames_requested; i++) {
                    _outputQueue.NonblockingEnqueue(_sb.Dac.RenderFrame());
                }
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
                                    _loggerService.Debug("Filter always on; ignoring {Action} low-pass filter command",
                                        _sb.Mixer.FilterEnabled ? "enable" : "disable");
                                }
                            } else {
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

            case (byte)MixerRegister.EssIdentification:
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

            case (byte)MixerRegister.IrqSelect:
                ret = 0;
                return _sb.Hw.Irq switch {
                    2 => 0x01,
                    5 => 0x02,
                    7 => 0x04,
                    10 => 0x08,
                    _ => ret,
                };

            case (byte)MixerRegister.DmaSelect:
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

            case (byte)MixerRegister.IrqStatus:
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
