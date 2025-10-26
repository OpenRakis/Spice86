namespace Spice86.Core.Emulator.Devices.Sound.Blaster;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.DirectMemoryAccess;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

using System.Collections.Frozen;

/// <summary>
///     Sound Blaster device implementation. <br />
///     http://www.fysnet.net/detectsb.htm
/// </summary>
public class SoundBlaster : DefaultIOPortHandler, IRequestInterrupt,
    IBlasterEnvVarProvider, IDisposable {
    private const int LeftSpeakerStatusPortOffset = 0x00;
    private const int LeftSpeakerDataPortOffset = 0x01;
    private const int RightSpeakerStatusPortOffset = 0x02;
    private const int RightSpeakerDataPortOffset = 0x03;
    private const int MixerRegisterPortOffset = 0x04;
    private const int MixerDataPortOffset = 0x05;
    private const int IgnorePortOffset = 0x07;
    private const int Mpu401DataPortOffset = 0xE0;
    private const int Mpu401StatusCommandPortOffset = 0xE1;
    private const double PendingIrqRetryIntervalMs = 0.05;

    private static readonly FrozenDictionary<byte, byte> CommandLengths = new Dictionary<byte, byte> {
        { Commands.SetTimeConstant, 1 },
        { Commands.SingleCycleDmaOutput8, 2 },
        { Commands.DspIdentification, 1 },
        { Commands.SetBlockTransferSize, 2 },
        { Commands.SetSampleRate, 2 },
        { Commands.SetInputSampleRate, 2 },
        { Commands.SingleCycleDmaOutput16, 3 },
        { Commands.AutoInitDmaOutput16, 3 },
        { Commands.SingleCycleDmaOutput16Fifo, 3 },
        { Commands.AutoInitDmaOutput16Fifo, 3 },
        { Commands.SingleCycleDmaOutput8_Alt, 3 },
        { Commands.AutoInitDmaOutput8_Alt, 3 },
        { Commands.SingleCycleDmaOutput8Fifo_Alt, 3 },
        { Commands.AutoInitDmaOutput8Fifo_Alt, 3 },
        { Commands.PauseForDuration, 2 },
        { Commands.SingleCycleDmaOutputADPCM4Ref, 2 },
        { Commands.SingleCycleDmaOutputADPCM2Ref, 2 },
        { Commands.SingleCycleDmaOutputADPCM3Ref, 2 }
    }.ToFrozenDictionary();

    private readonly List<byte> _commandData = [];
    private readonly SoundBlasterHardwareConfig _config;
    private readonly HardwareMixer _ctMixer;
    private readonly DeviceThread _deviceThread;
    private readonly DmaPlaybackState _dmaState = new();

    private readonly PicEventHandler _dmaTransferEventHandler;
    private readonly Dsp _dsp;
    private readonly DualPic _dualPic;
    private readonly Queue<byte> _outputData = new();
    private readonly int _outputSampleRate;
    private readonly PicEventHandler _pendingIrqEventHandler;

    private readonly DmaChannel _primaryDmaChannel;
    private readonly byte[] _readFromDspBuffer = new byte[512];
    private readonly short[] _renderingBuffer = new short[65536 * 2];
    private readonly DmaChannel? _secondaryDmaChannel;
    private readonly PicEventHandler _suppressDmaEventHandler;
    private BlasterState _blasterState;
    private bool _blockTransferSizeSet;
    private byte _commandDataLength;
    private byte _currentCommand;

    private bool _disposed;
    private bool _dmaTransferEventScheduled;
    private int _pauseDuration;
    private bool _pendingIrq;
    private bool _pendingIrqAllowMasked;
    private bool _pendingIrqEventScheduled;
    private bool _pendingIrqIs16Bit;
    private int _resetCount;
    private bool _suppressDmaEventScheduled;

    /// <summary>
    ///     Initializes a new instance of the SoundBlaster class.
    /// </summary>
    /// <param name="ioPortDispatcher">
    ///     The class that is responsible for dispatching ports reads and writes to classes that
    ///     respond to them.
    /// </param>
    /// <param name="softwareMixer">The emulator's sound mixer.</param>
    /// <param name="state">The CPU registers and flags.</param>
    /// <param name="dmaSystem">The DMA system used for PCM data transfers by the DSP.</param>
    /// <param name="dualPic">The two programmable interrupt controllers.</param>
    /// <param name="failOnUnhandledPort">Whether we throw an exception when an IO port wasn't handled.</param>
    /// <param name="loggerService">The logging service used for logging events.</param>
    /// <param name="soundBlasterHardwareConfig">The IRQ, low DMA, and high DMA configuration.</param>
    /// <param name="pauseHandler">The handler for the emulation pause state.</param>
    public SoundBlaster(IOPortDispatcher ioPortDispatcher, SoftwareMixer softwareMixer, State state,
        DmaSystem dmaSystem,
        DualPic dualPic, bool failOnUnhandledPort, ILoggerService loggerService,
        SoundBlasterHardwareConfig soundBlasterHardwareConfig, IPauseHandler pauseHandler) : base(state,
        failOnUnhandledPort, loggerService) {
        _config = soundBlasterHardwareConfig;
        _dualPic = dualPic;
        _dmaTransferEventHandler = DmaTransferEvent;
        _suppressDmaEventHandler = SuppressDmaEvent;
        _pendingIrqEventHandler = PendingIrqEvent;
        _primaryDmaChannel = dmaSystem.GetChannel(_config.LowDma)
                             ?? throw new InvalidOperationException(
                                 $"DMA channel {_config.LowDma} unavailable for Sound Blaster.");
        _secondaryDmaChannel = ShouldUseHighDmaChannel()
            ? dmaSystem.GetChannel(_config.HighDma)
            : null;

        if (_primaryDmaChannel.ChannelNumber == 4 ||
            (_secondaryDmaChannel is not null && _secondaryDmaChannel.ChannelNumber == 4)) {
            throw new InvalidOperationException("Sound Blaster cannot attach to cascade DMA channel 4.");
        }

        _dsp = new Dsp(dmaSystem, _config.LowDma, ShouldUseHighDmaChannel() ? _config.HighDma : null, _loggerService);
        RegisterDmaCallbacks();
        _dualPic.IrqMaskChanged += OnPicIrqMaskChanged;

        _deviceThread = new DeviceThread(nameof(SoundBlaster), PlaybackLoopBody, pauseHandler, loggerService);
        PCMSoundChannel = softwareMixer.CreateChannel(nameof(SoundBlaster));
        _outputSampleRate = PCMSoundChannel.SampleRate;
        FMSynthSoundChannel = softwareMixer.CreateChannel(nameof(Sound.Opl3Fm), _outputSampleRate);
        Opl3Fm = new Opl3Fm(FMSynthSoundChannel, state, ioPortDispatcher, failOnUnhandledPort, loggerService,
            pauseHandler, dualPic, enableOplIrq: true, useAdlibGold: true);
        _ctMixer = new HardwareMixer(soundBlasterHardwareConfig, PCMSoundChannel, FMSynthSoundChannel, loggerService);
        InitPortHandlers(ioPortDispatcher);

        // Ensure the PIC allows the configured IRQ line to propagate.
        _dualPic.SetIrqMask(IRQ, false);

        const int coldWarmupMs = 4;
        int coldWarmupFrames = Math.Max(1, _outputSampleRate * coldWarmupMs / 1000);
        int hotWarmupFrames = Math.Max(1, coldWarmupFrames / 32);
        _dmaState.ColdWarmupFrames = coldWarmupFrames;
        _dmaState.HotWarmupFrames = hotWarmupFrames;
        _dmaState.SpeakerEnabled = HasSpeaker;
        if (_dmaState.SpeakerEnabled) {
            _dmaState.WarmupRemainingFrames = _dmaState.ColdWarmupFrames;
        }
    }

    /// <summary>
    ///     The Sound Blaster's PCM sound channel.
    /// </summary>
    public SoundChannel PCMSoundChannel { get; }

    /// <summary>
    ///     The Sound Blaster's OPL3 FM sound channel.
    /// </summary>
    public SoundChannel FMSynthSoundChannel { get; }

    /// <summary>
    ///     The internal FM synth chip for music.
    /// </summary>
    public Opl3Fm Opl3Fm { get; }

    /// <summary>
    ///     The type of Sound Blaster card currently emulated.
    /// </summary>
    public SbType SbType => _config.SbType;

    /// <summary>
    ///     Gets the hardware IRQ assigned to the device.
    /// </summary>
    public byte IRQ => _config.Irq;

    private bool HasSpeaker => SbType == SbType.Sb16;

    /// <summary>
    ///     Gets the value that should be exposed through the BLASTER environment variable.
    /// </summary>
    public string BlasterString {
        get {
            string highChannelSegment = ShouldUseHighDmaChannel() ? $" H{_config.HighDma}" : string.Empty;
            string midiSegment = $" P{MapPort(Mpu401DataPortOffset):X3}";
            return
                $"A{_config.BaseAddress:X3} I{_config.Irq} D{_config.LowDma}{highChannelSegment}{midiSegment} T{(int)SbType}";
        }
    }

    /// <inheritdoc />
    public void Dispose() {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    ///     Raises an interrupt request, delivering it immediately if the DMA and PIC states allow it.
    /// </summary>
    public void RaiseInterruptRequest() {
        RaiseInterruptRequest(_dsp.Is16Bit);
    }

    private int MapPort(int offset) {
        return (_config.BaseAddress + offset) & 0xFFFF;
    }

    private bool ShouldUseHighDmaChannel() {
        return SbType == SbType.Sb16 &&
               _config.HighDma >= 5 &&
               _config.HighDma != _config.LowDma;
    }

    private void RegisterDmaCallbacks() {
        SetupDmaChannelCallbacks(_primaryDmaChannel);
        if (_secondaryDmaChannel is not null && !ReferenceEquals(_secondaryDmaChannel, _primaryDmaChannel)) {
            SetupDmaChannelCallbacks(_secondaryDmaChannel);
        }
    }

    private void SetupDmaChannelCallbacks(DmaChannel channel) {
        channel.ReserveFor("SoundBlaster", OnDmaChannelEvicted);
        channel.RegisterCallback(OnDmaChannelEvent);
    }

    private void UnregisterDmaCallbacks() {
        _primaryDmaChannel.RegisterCallback(null);
        _secondaryDmaChannel?.RegisterCallback(null);
        _dualPic.IrqMaskChanged -= OnPicIrqMaskChanged;
    }

    private void OnDmaChannelEvicted() {
        StopDmaScheduler();
        ResetDmaState();
        _dsp.IsDmaTransferActive = false;
        CancelPendingIrqCheck();
        _pendingIrq = false;
        _pendingIrqIs16Bit = false;
        _pendingIrqAllowMasked = false;
        _loggerService.Warning("Sound Blaster DMA channel was evicted; playback state reset.");
    }

    private void OnDmaChannelEvent(DmaChannel channel, DmaChannel.DmaEvent dmaEvent) {
        if (!ReferenceEquals(channel, _primaryDmaChannel) && !ReferenceEquals(channel, _secondaryDmaChannel)) {
            return;
        }

        bool isCurrent = ReferenceEquals(channel, _dsp.CurrentChannel);

        switch (dmaEvent) {
            case DmaChannel.DmaEvent.ReachedTerminalCount:
                if (isCurrent) {
                    HandleDmaTerminalCount();
                }

                break;
            case DmaChannel.DmaEvent.IsMasked:
                if (isCurrent) {
                    OnDmaMaskChanged(channel, true);
                }

                break;
            case DmaChannel.DmaEvent.IsUnmasked:
                if (isCurrent) {
                    OnDmaMaskChanged(channel, false);
                }

                break;
        }
    }

    /// <inheritdoc />
    public override byte ReadByte(ushort port) {
        switch (port - _config.BaseAddress) {
            case Mpu401DataPortOffset:
                return 0xFF;
            case Mpu401StatusCommandPortOffset:
                return 0xC0; //No data, and the interface is not ready
            case DspPorts.DspReadData:
                return _outputData.Count > 0 ? _outputData.Dequeue() : (byte)0;
            case DspPorts.DspWriteData:
                return 0x00;
            case DspPorts.DspReadStatus:
                _ctMixer.InterruptStatusRegister = InterruptStatus.None;
                var readStatus = new BufferStatus {
                    HasData = _outputData.Count != 0
                };
                return readStatus.Data;
            case DspPorts.MixerAddress:
                return (byte)_ctMixer.CurrentAddress;
            case DspPorts.MixerData:
                return _ctMixer.ReadData();
            case DspPorts.DspReset:
                return 0xFF;
            default:
                if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                    _loggerService.Warning("Unhandled byte read of SB port {PortNumber:X4}", port);
                }

                return 0xFF;
        }
    }

    /// <inheritdoc />
    public override void WriteByte(ushort port, byte value) {
        _deviceThread.StartThreadIfNeeded();
        switch (port - _config.BaseAddress) {
            case Mpu401DataPortOffset:
            case Mpu401StatusCommandPortOffset:
                //ignored
                return;
            case DspPorts.DspReset:
                switch (value) {
                    // Expect a 1, then 0 written to reset the DSP.
                    case 1:
                        _blasterState = BlasterState.ResetRequest;
                        break;
                    case 0 when _blasterState == BlasterState.ResetRequest: {
                        _blasterState = BlasterState.Resetting;
                        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                            _loggerService.Verbose("SoundBlaster DSP was reset");
                        }

                        Reset();
                        break;
                    }
                }

                break;
            case DspPorts.DspWriteData:
                switch (_blasterState) {
                    case BlasterState.WaitingForCommand: {
                        _currentCommand = value;
                        _blasterState = BlasterState.ReadingCommand;
                        _commandData.Clear();
                        CommandLengths.TryGetValue(value, out _commandDataLength);
                        if (_commandDataLength == 0) {
                            if (!ProcessCommand()) {
                                base.WriteByte(port, value);
                            }
                        }

                        break;
                    }
                    case BlasterState.ReadingCommand: {
                        _commandData.Add(value);
                        if (_commandData.Count >= _commandDataLength) {
                            if (!ProcessCommand()) {
                                base.WriteByte(port, value);
                            }
                        }

                        break;
                    }
                }

                break;
            case DspPorts.MixerData:
                _ctMixer.Write(value);
                break;
            case DspPorts.MixerAddress:
                _ctMixer.CurrentAddress = value;
                break;
            case IgnorePortOffset:
                if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                    _loggerService.Debug("Sound Blaster ignored port write {PortNumber:X2} with value {Value:X2}",
                        port, value);
                }

                break;
            default:
                if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                    _loggerService.Warning("Unhandled byte write of SB port {PortNumber:X4}", port);
                }

                base.WriteByte(port, value);
                break;
        }
    }

    /// <inheritdoc />
    public override ushort ReadWord(ushort port) {
        uint value = ReadByte(port);
        value |= (uint)(ReadByte((ushort)(port + 1)) << 8);
        return (ushort)value;
    }

    /// <inheritdoc />
    public override void WriteWord(ushort port, ushort value) {
        switch (port - _config.BaseAddress) {
            case DspPorts.MixerAddress:
                _ctMixer.CurrentAddress = value;
                break;
            default:
                base.WriteWord(port, value);
                break;
        }
    }

    private void Dispose(bool disposing) {
        if (_disposed) {
            return;
        }

        if (disposing) {
            StopDmaScheduler();
            ResetDmaState();
            UnregisterDmaCallbacks();
            CancelSuppressEvent();
            _deviceThread.Dispose();
            _dsp.Dispose();
        }

        _disposed = true;
    }

    private void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
        ioPortDispatcher.AddIOPortHandler(MapPort(DspPorts.DspReset), this);
        ioPortDispatcher.AddIOPortHandler(MapPort(DspPorts.DspReadStatus), this);
        ioPortDispatcher.AddIOPortHandler(MapPort(DspPorts.DspWriteData), this);
        ioPortDispatcher.AddIOPortHandler(MapPort(MixerRegisterPortOffset), this);
        ioPortDispatcher.AddIOPortHandler(MapPort(MixerDataPortOffset), this);
        ioPortDispatcher.AddIOPortHandler(MapPort(DspPorts.DspReadData), this);

        ioPortDispatcher.AddIOPortHandler(MapPort(LeftSpeakerStatusPortOffset), this);
        ioPortDispatcher.AddIOPortHandler(MapPort(LeftSpeakerDataPortOffset), this);
        ioPortDispatcher.AddIOPortHandler(MapPort(RightSpeakerStatusPortOffset), this);
        ioPortDispatcher.AddIOPortHandler(MapPort(RightSpeakerDataPortOffset), this);
        ioPortDispatcher.AddIOPortHandler(MapPort(IgnorePortOffset), this);
        ioPortDispatcher.AddIOPortHandler(MapPort(Mpu401DataPortOffset), this);
        ioPortDispatcher.AddIOPortHandler(MapPort(Mpu401StatusCommandPortOffset), this);
        // Those are managed by OPL3FM class.
        //ioPortDispatcher.AddIOPortHandler(FM_MUSIC_STATUS_PORT_NUMBER_2, this);
        //ioPortDispatcher.AddIOPortHandler(FM_MUSIC_DATA_PORT_NUMBER_2, this);
        //ioPortDispatcher.AddIOPortHandler(FM_MUSIC_STATUS_PORT_NUMBER, this);
        //ioPortDispatcher.AddIOPortHandler(FM_MUSIC_DATA_PORT_NUMBER, this);
    }

    private void HandleDmaTerminalCount() {
        if (_dmaState.Mode == DmaPlaybackMode.None) {
            return;
        }

        if (_dmaState.AutoInit) {
            // DmaTransferEvent finalizes the block once the current pump returns.
            return;
        }

        if (!_dmaState.IrqRaisedForCurrentBlock) {
            RaiseInterruptRequest(Is16BitMode(_dmaState.Mode), true);
            _dmaState.IrqRaisedForCurrentBlock = true;
        }

        _dsp.IsDmaTransferActive = false;
        StopDmaScheduler();
        ResetDmaState();
    }

    private void PlaybackLoopBody() {
        _dsp.Read(_readFromDspBuffer);
        int length = Resample(_readFromDspBuffer, _outputSampleRate, _renderingBuffer);

        if (length > 0) {
            bool muteOutput = !_dmaState.SpeakerEnabled;
            if (_dmaState.WarmupRemainingFrames > 0) {
                int framesRendered = _dsp.IsStereo ? length / 2 : length;
                if (framesRendered <= 0) {
                    framesRendered = length;
                }

                _dmaState.WarmupRemainingFrames = Math.Max(0, _dmaState.WarmupRemainingFrames - framesRendered);
                muteOutput = true;
            }

            if (muteOutput) {
                Array.Clear(_renderingBuffer, 0, length);
            }
        }

        PCMSoundChannel.Render(_renderingBuffer.AsSpan(0, length));

        if (_pauseDuration <= 0) {
            return;
        }

        Array.Clear(_renderingBuffer, 0, _renderingBuffer.Length);
        int count = (_pauseDuration / (1024 / 2)) + 1;
        for (int i = 0; i < count; i++) {
            PCMSoundChannel.Render(_renderingBuffer.AsSpan(0, 1024));
        }

        _pauseDuration = 0;
        RaiseInterruptRequest();
    }

    /// <summary>
    ///     Resamples the data in sourceBuffer to destinationBuffer with the given sampleRate. Returns the destinationBuffer
    ///     length.
    /// </summary>
    /// <param name="sourceBuffer"></param>
    /// <param name="sampleRate"></param>
    /// <param name="destinationBuffer"></param>
    /// <returns>Length of the data written in destinationBuffer</returns>
    private int Resample(Span<byte> sourceBuffer, int sampleRate, short[] destinationBuffer) {
        if (_dsp is { Is16Bit: true, IsStereo: true }) {
            return LinearUpsampler.Resample16Stereo(_dsp.SampleRate, sampleRate, sourceBuffer.Cast<byte, short>(),
                destinationBuffer);
        }

        if (_dsp.Is16Bit) {
            return LinearUpsampler.Resample16Mono(_dsp.SampleRate, sampleRate, sourceBuffer.Cast<byte, short>(),
                destinationBuffer);
        }

        if (_dsp.IsStereo) {
            return LinearUpsampler.Resample8Stereo(_dsp.SampleRate, sampleRate, sourceBuffer, destinationBuffer);
        }

        return LinearUpsampler.Resample8Mono(_dsp.SampleRate, sampleRate, sourceBuffer, destinationBuffer);
    }

    private uint GetAvailableDmaBytes() {
        uint available = _dmaState.RemainingBytes;
        if (available == 0 && _dmaState.AutoInit) {
            available = _dmaState.AutoSizeBytes;
        }

        return available;
    }

    private uint AlignToDmaGranularity(uint requestedBytes, uint availableBytes) {
        uint capped = Math.Min(requestedBytes, availableBytes);
        if (capped == 0) {
            return 0;
        }

        DmaChannel? channel = _dsp.CurrentChannel;
        if (channel is null) {
            return capped;
        }

        uint alignment = 1u << channel.ShiftCount;
        if (alignment <= 1) {
            return capped;
        }

        uint aligned = capped - (capped % alignment);
        return aligned != 0 ? aligned : capped;
    }

    private uint NormalizeDmaRequest(uint desiredBytes, uint availableBytes) {
        if (availableBytes == 0) {
            return 0;
        }

        uint aligned = AlignToDmaGranularity(desiredBytes, availableBytes);
        if (aligned != 0) {
            return aligned;
        }

        return AlignToDmaGranularity(availableBytes, availableBytes);
    }

    private void StartDmaScheduler() {
        if (!_dsp.IsDmaTransferActive || _dmaState.Mode == DmaPlaybackMode.None || _dmaState.DmaMasked) {
            return;
        }

        if (_dmaTransferEventScheduled) {
            return;
        }

        ScheduleDmaPump();
    }

    private void StopDmaScheduler() {
        if (!_dmaTransferEventScheduled) {
            return;
        }

        _dualPic.RemoveEvents(_dmaTransferEventHandler);
        _dmaTransferEventScheduled = false;
    }

    private void DmaTransferEvent(uint chunkHint) {
        _dmaTransferEventScheduled = false;

        if (!_dsp.IsDmaTransferActive || _dmaState.Mode == DmaPlaybackMode.None || _dmaState.DmaMasked) {
            return;
        }

        _dmaState.LastPumpTimeMs = _dualPic.GetFullIndex();

        uint available = GetAvailableDmaBytes();
        uint chunk = chunkHint != 0 ? NormalizeDmaRequest(Math.Min(chunkHint, available), available) : 0;
        if (chunk == 0) {
            chunk = ComputeDmaChunk(out _);
        }

        if (chunk == 0) {
            if (!HandleDmaBlockCompletion()) {
                return;
            }

            ScheduleDmaPump();
            return;
        }

        int bytesTransferred = _dsp.PumpDma((int)chunk);
        if (bytesTransferred <= 0) {
            double retryDelay = Math.Min(chunk * 1000.0 / Math.Max(_dmaState.RateBytesPerSecond, 1.0), 0.25);
            _dualPic.AddEvent(_dmaTransferEventHandler, retryDelay);
            _dmaTransferEventScheduled = true;
            return;
        }

        if ((uint)bytesTransferred > chunk) {
            bytesTransferred = (int)chunk;
        }

        if (_dmaState.RemainingBytes > 0) {
            int remaining = (int)_dmaState.RemainingBytes - bytesTransferred;
            _dmaState.RemainingBytes = remaining > 0 ? (uint)remaining : 0u;
        }

        if (_dmaState.RemainingBytes == 0) {
            if (!HandleDmaBlockCompletion()) {
                return;
            }
        }

        ScheduleDmaPump();
    }

    private void ScheduleDmaPump() {
        if (_dmaTransferEventScheduled) {
            return;
        }

        if (!_dsp.IsDmaTransferActive || _dmaState.Mode == DmaPlaybackMode.None || _dmaState.DmaMasked) {
            return;
        }

        CancelSuppressEvent();

        uint chunk = ComputeDmaChunk(out double delayMs);
        if (chunk == 0) {
            return;
        }

        _dualPic.AddEvent(_dmaTransferEventHandler, delayMs, chunk);
        _dmaTransferEventScheduled = true;
    }

    private uint ComputeDmaChunk(out double delayMs) {
        delayMs = 0.0;

        uint available = GetAvailableDmaBytes();
        if (available == 0) {
            return 0;
        }

        uint chunk = available > _dmaState.MinChunkBytes ? _dmaState.MinChunkBytes : available;
        chunk = NormalizeDmaRequest(chunk, available);
        if (chunk == 0) {
            chunk = available;
        }

        delayMs = chunk * 1000.0 / Math.Max(_dmaState.RateBytesPerSecond, 1.0);
        if (delayMs < 0.001) {
            delayMs = 0.001;
        } else if (delayMs > 20.0) {
            delayMs = 20.0;
        }

        return chunk;
    }

    private void OnDmaMaskChanged(DmaChannel channel, bool masked) {
        if (_dsp.CurrentChannel != channel) {
            return;
        }

        _dmaState.DmaMasked = masked;
        _loggerService.Debug("Sound Blaster DMA mask changed to {Masked} on channel {ChannelNumber}", masked,
            channel.ChannelNumber);
        if (_dmaState.Mode == DmaPlaybackMode.None || !_dsp.IsDmaTransferActive) {
            return;
        }

        CancelSuppressEvent();
        if (masked) {
            StopDmaScheduler();
            CatchUpWhileMasked();
        } else {
            FlushRemainingDmaTransfer();
            TryDeliverPendingInterrupt();
        }
    }

    private void CancelSuppressEvent() {
        if (!_suppressDmaEventScheduled) {
            return;
        }

        _dualPic.RemoveEvents(_suppressDmaEventHandler);
        _suppressDmaEventScheduled = false;
    }

    private void ScheduleSuppressDma(uint bytes) {
        if (!_dsp.IsDmaTransferActive || _dmaState.Mode == DmaPlaybackMode.None) {
            return;
        }

        uint availableBytes = GetAvailableDmaBytes();
        uint normalizedBytes = NormalizeDmaRequest(bytes, availableBytes);
        if (normalizedBytes == 0) {
            return;
        }

        CancelSuppressEvent();
        StopDmaScheduler();

        double delay = _dmaState.RateBytesPerSecond <= 0
            ? 0.001
            : normalizedBytes * 1000.0 / _dmaState.RateBytesPerSecond;

        delay = delay switch {
            < 0.001 => 0.001,
            > 20.0 => 20.0,
            _ => delay
        };

        _dualPic.AddEvent(_suppressDmaEventHandler, delay, normalizedBytes);
        _suppressDmaEventScheduled = true;
    }

    private void CatchUpWhileMasked() {
        uint remainingBytes = _dmaState.RemainingBytes;
        if (remainingBytes == 0 || _dmaState.RateBytesPerSecond <= 0) {
            return;
        }

        double nowMs = _dualPic.GetFullIndex();
        double elapsedMs = nowMs - _dmaState.LastPumpTimeMs;
        if (elapsedMs <= 0.0) {
            return;
        }

        uint minimumTailBytes = GetMinimumTailBytes();
        if (remainingBytes <= minimumTailBytes) {
            return;
        }

        double catchUpFloat = _dmaState.RateBytesPerSecond * elapsedMs / 1000.0;
        uint catchUpFromElapsed = catchUpFloat > uint.MaxValue
            ? uint.MaxValue
            : (uint)Math.Floor(catchUpFloat);
        if (catchUpFromElapsed == 0) {
            return;
        }

        uint catchUpLimit = Math.Min(_dmaState.MinChunkBytes, catchUpFromElapsed);
        uint maxCatchUp = remainingBytes - minimumTailBytes;
        uint catchUpCandidate = Math.Min(catchUpLimit, maxCatchUp);
        uint catchUp = NormalizeDmaRequest(catchUpCandidate, remainingBytes);
        if (catchUp == 0) {
            return;
        }

        int pumped = _dsp.PumpDma((int)catchUp);
        if (pumped <= 0) {
            return;
        }

        if (_dmaState.RemainingBytes > 0) {
            int remaining = (int)_dmaState.RemainingBytes - pumped;
            _dmaState.RemainingBytes = remaining > 0 ? (uint)remaining : 0u;
        }

        _dmaState.LastPumpTimeMs = nowMs;
    }

    private void SuppressDmaEvent(uint requestedBytes) {
        _suppressDmaEventScheduled = false;

        if (!_dsp.IsDmaTransferActive || _dmaState.Mode == DmaPlaybackMode.None) {
            return;
        }

        uint remainingBytes = _dmaState.RemainingBytes;
        if (requestedBytes == 0 || requestedBytes > remainingBytes) {
            requestedBytes = remainingBytes;
        }

        requestedBytes = NormalizeDmaRequest(requestedBytes, remainingBytes);

        if (requestedBytes == 0) {
            if (!HandleDmaBlockCompletion()) {
                return;
            }

            if (_dmaState.SpeakerEnabled || HasSpeaker) {
                ScheduleDmaPump();
            }

            return;
        }

        int bytesTransferred = _dsp.PumpDma((int)requestedBytes);
        if (bytesTransferred <= 0) {
            _loggerService.Verbose(
                "Sound Blaster DMA pump transferred 0 of {RequestedBytes} bytes during suppression; scheduling retry.",
                requestedBytes);
            double retryDelay = Math.Min(requestedBytes * 1000.0 / Math.Max(_dmaState.RateBytesPerSecond, 1.0), 0.25);
            _dualPic.AddEvent(_suppressDmaEventHandler, retryDelay, requestedBytes);
            _suppressDmaEventScheduled = true;
            return;
        }

        if (_dmaState.RemainingBytes > 0) {
            int remaining = (int)_dmaState.RemainingBytes - bytesTransferred;
            _dmaState.RemainingBytes = remaining > 0 ? (uint)remaining : 0u;
        }

        if (_dmaState.RemainingBytes == 0) {
            bool continuePlayback = HandleDmaBlockCompletion();
            if (!continuePlayback) {
                return;
            }
        }

        if (!_dmaState.SpeakerEnabled && !HasSpeaker) {
            uint nextBytes = Math.Min(_dmaState.MinChunkBytes, _dmaState.RemainingBytes);
            if (nextBytes == 0) {
                nextBytes = _dmaState.RemainingBytes;
            }

            if (nextBytes > 0) {
                ScheduleSuppressDma(nextBytes);
            }
        } else {
            ScheduleDmaPump();
        }
    }

    private uint GetMinimumTailBytes() {
        double bytesPerSample = GetBytesPerSample(_dmaState.Mode, _dmaState.Stereo);
        if (bytesPerSample <= 0) {
            bytesPerSample = 1.0;
        }

        uint minimum = (uint)Math.Max(1, Math.Round(bytesPerSample));
        uint tailBytes = minimum * 2;
        return AlignToDmaGranularity(tailBytes, tailBytes);
    }

    private void SetSpeakerEnabled(bool enabled) {
        if (HasSpeaker) {
            _dmaState.SpeakerEnabled = true;
            return;
        }

        if (_dmaState.SpeakerEnabled == enabled) {
            return;
        }

        if (enabled) {
            CancelSuppressEvent();
            _dmaState.SpeakerEnabled = true;
            int warmup = _resetCount <= 1 ? _dmaState.ColdWarmupFrames : _dmaState.HotWarmupFrames;
            _dmaState.WarmupRemainingFrames = Math.Max(_dmaState.WarmupRemainingFrames, warmup);
        } else {
            _dmaState.SpeakerEnabled = false;
            _dmaState.WarmupRemainingFrames = 0;
        }

        FlushRemainingDmaTransfer();
    }

    private void FlushRemainingDmaTransfer() {
        if (!_dsp.IsDmaTransferActive || _dmaState.Mode == DmaPlaybackMode.None) {
            return;
        }

        if (_dmaState.RemainingBytes == 0) {
            return;
        }

        if (!_dmaState.SpeakerEnabled && !HasSpeaker) {
            uint bytes = Math.Min(Math.Max(_dmaState.MinChunkBytes, 1u), _dmaState.RemainingBytes);
            if (bytes == 0) {
                bytes = _dmaState.RemainingBytes;
            }

            if (bytes > 0) {
                ScheduleSuppressDma(bytes);
            }

            return;
        }

        if (_dmaState.RemainingBytes >= _dmaState.MinChunkBytes && _dmaTransferEventScheduled) {
            return;
        }

        StopDmaScheduler();
        ScheduleDmaPump();
    }

    private bool HandleDmaBlockCompletion() {
        if (!_dmaState.IrqRaisedForCurrentBlock) {
            RaiseInterruptRequest(Is16BitMode(_dmaState.Mode), true);
            _dmaState.IrqRaisedForCurrentBlock = true;
        }

        if (_dmaState.AutoInit) {
            if (_dmaState.AutoSizeBytes == 0) {
                _loggerService.Warning(
                    "Sound Blaster auto-init DMA block completion encountered zero block size; stopping playback.");
                ResetDmaState();
                _dsp.IsDmaTransferActive = false;
                StopDmaScheduler();
                return false;
            }

            _dmaState.RemainingBytes = _dmaState.AutoSizeBytes;
            _dmaState.IrqRaisedForCurrentBlock = false;
            return true;
        }

        ResetDmaState();
        _dsp.IsDmaTransferActive = false;
        StopDmaScheduler();
        return false;
    }

    private void ResetDmaState() {
        CancelSuppressEvent();
        CancelPendingIrqCheck();
        _pendingIrq = false;
        _dmaState.Mode = DmaPlaybackMode.None;
        _dmaState.AutoInit = false;
        _dmaState.Stereo = false;
        _dmaState.RateBytesPerSecond = 0;
        _dmaState.MinChunkBytes = 0;
        _dmaState.RemainingBytes = 0;
        _dmaState.AutoSizeBytes = 0;
        _dmaState.IrqRaisedForCurrentBlock = false;
        _dmaState.DmaMasked = false;
        _dmaState.LastPumpTimeMs = _dualPic.GetFullIndex();
    }

    private void ConfigureDmaTransfer(DmaPlaybackMode mode, bool autoInit, bool stereo) {
        _dmaState.Mode = mode;
        _dmaState.AutoInit = autoInit;
        _dmaState.Stereo = stereo;
        _dmaState.IrqRaisedForCurrentBlock = false;

        DmaChannel? channel = _dsp.CurrentChannel;
        uint transferBytes = channel is null ? 0u : (uint)((channel.CurrentCount + 1) << channel.ShiftCount);

        if (transferBytes == 0 && channel is not null) {
            transferBytes = (uint)((channel.BaseCount + 1) << channel.ShiftCount);
        }

        if (transferBytes == 0 && _dsp.BlockTransferSize > 0) {
            transferBytes = (uint)_dsp.BlockTransferSize;
        }

        if (autoInit) {
            _dmaState.AutoSizeBytes = transferBytes;
        }

        _dmaState.RemainingBytes = transferBytes;
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug(
                "Configured Sound Blaster DMA transfer: mode {Mode}, auto-init {AutoInit}, stereo {Stereo}, length {TransferBytes}",
                mode, autoInit, stereo, transferBytes);
        }

        UpdateActiveDmaRate();
        _dmaState.LastPumpTimeMs = _dualPic.GetFullIndex();
    }

    private void UpdateActiveDmaRate() {
        if (_dmaState.Mode == DmaPlaybackMode.None) {
            return;
        }

        double bytesPerSample = GetBytesPerSample(_dmaState.Mode, _dmaState.Stereo);
        if (bytesPerSample <= 0) {
            bytesPerSample = 1.0;
        }

        double sampleRate = Math.Max(_dsp.SampleRate, 1);
        _dmaState.RateBytesPerSecond = sampleRate * bytesPerSample;
        _dmaState.MinChunkBytes = (uint)Math.Max(Math.Round(_dmaState.RateBytesPerSecond * 3.0 / 1000.0), 1.0);
    }

    private static double GetBytesPerSample(DmaPlaybackMode mode, bool stereo) {
        double baseValue = mode switch {
            DmaPlaybackMode.Pcm8Bit => 1.0,
            DmaPlaybackMode.Pcm16Bit => 2.0,
            DmaPlaybackMode.Pcm16BitAliased => 2.0,
            DmaPlaybackMode.Adpcm2Bit => 0.25,
            DmaPlaybackMode.Adpcm3Bit => 0.375,
            DmaPlaybackMode.Adpcm4Bit => 0.5,
            _ => 1.0
        };

        if (mode is DmaPlaybackMode.Adpcm2Bit or DmaPlaybackMode.Adpcm3Bit or DmaPlaybackMode.Adpcm4Bit) {
            return baseValue;
        }

        return stereo ? baseValue * 2.0 : baseValue;
    }

    private static bool Is16BitMode(DmaPlaybackMode mode) {
        return mode is DmaPlaybackMode.Pcm16Bit or DmaPlaybackMode.Pcm16BitAliased;
    }

    private void BeginDmaPlayback(DmaPlaybackMode mode, bool is16Bit, bool stereo, bool autoInit,
        CompressionLevel compression = CompressionLevel.None, bool referenceByte = false) {
        _dsp.Begin(is16Bit, stereo, compression, referenceByte);
        ConfigureDmaTransfer(mode, autoInit, stereo);
    }

    /// <summary>
    ///     Performs the action associated with the current DSP command.
    /// </summary>
    private bool ProcessCommand() {
        _outputData.Clear();
        bool startDmaScheduler = false;
        bool stopDmaScheduler = false;
        switch (_currentCommand) {
            case Commands.GetVersionNumber:
                _outputData.Enqueue(4);
                _outputData.Enqueue(5);
                break;

            case Commands.DspIdentification:
                _outputData.Enqueue((byte)~_commandData[0]);
                break;

            case Commands.SetTimeConstant:
                _dsp.SampleRate = 256000000 / (65536 - (_commandData[0] << 8));
                UpdateActiveDmaRate();
                break;

            case Commands.SetSampleRate:
                _dsp.SampleRate = (_commandData[0] << 8) | _commandData[1];
                UpdateActiveDmaRate();
                break;

            case Commands.SetBlockTransferSize:
                _dsp.BlockTransferSize = (_commandData[0] | (_commandData[1] << 8)) + 1;
                _blockTransferSizeSet = true;
                if (_dmaState.Mode != DmaPlaybackMode.None && _dmaState.AutoInit) {
                    _dmaState.AutoSizeBytes = (uint)_dsp.BlockTransferSize;
                    if (_dmaState.RemainingBytes == 0) {
                        _dmaState.RemainingBytes = _dmaState.AutoSizeBytes;
                    }
                }

                break;

            case Commands.SingleCycleDmaOutput8:
            case Commands.HighSpeedSingleCycleDmaOutput8:
                BeginDmaPlayback(DmaPlaybackMode.Pcm8Bit, false, false, false);
                startDmaScheduler = true;
                break;

            case Commands.SingleCycleDmaOutput8_Alt:
            case Commands.SingleCycleDmaOutput8Fifo_Alt: {
                bool stereo = (_commandData[0] & (1 << 5)) != 0;
                BeginDmaPlayback(DmaPlaybackMode.Pcm8Bit, false, stereo, false);
                startDmaScheduler = true;
                break;
            }

            case Commands.SingleCycleDmaOutputADPCM4Ref:
                BeginDmaPlayback(DmaPlaybackMode.Adpcm4Bit, false, false, false, CompressionLevel.ADPCM4, true);
                startDmaScheduler = true;
                break;

            case Commands.SingleCycleDmaOutputADPCM4:
                BeginDmaPlayback(DmaPlaybackMode.Adpcm4Bit, false, false, false, CompressionLevel.ADPCM4);
                startDmaScheduler = true;
                break;

            case Commands.SingleCycleDmaOutputADPCM2Ref:
                BeginDmaPlayback(DmaPlaybackMode.Adpcm2Bit, false, false, false, CompressionLevel.ADPCM2, true);
                startDmaScheduler = true;
                break;

            case Commands.SingleCycleDmaOutputADPCM2:
                BeginDmaPlayback(DmaPlaybackMode.Adpcm2Bit, false, false, false, CompressionLevel.ADPCM2);
                startDmaScheduler = true;
                break;

            case Commands.SingleCycleDmaOutputADPCM3Ref:
                BeginDmaPlayback(DmaPlaybackMode.Adpcm3Bit, false, false, false, CompressionLevel.ADPCM3, true);
                startDmaScheduler = true;
                break;

            case Commands.SingleCycleDmaOutputADPCM3:
                BeginDmaPlayback(DmaPlaybackMode.Adpcm3Bit, false, false, false, CompressionLevel.ADPCM3);
                startDmaScheduler = true;
                break;

            case Commands.AutoInitDmaOutput8:
            case Commands.HighSpeedAutoInitDmaOutput8:
                if (!_blockTransferSizeSet) {
                    _dsp.BlockTransferSize = (_commandData[1] | (_commandData[2] << 8)) + 1;
                }

                BeginDmaPlayback(DmaPlaybackMode.Pcm8Bit, false, false, true);
                startDmaScheduler = true;
                break;

            case Commands.AutoInitDmaOutput8_Alt:
            case Commands.AutoInitDmaOutput8Fifo_Alt: {
                if (!_blockTransferSizeSet) {
                    _dsp.BlockTransferSize = (_commandData[1] | (_commandData[2] << 8)) + 1;
                }

                bool stereo = (_commandData[0] & (1 << 5)) != 0;
                BeginDmaPlayback(DmaPlaybackMode.Pcm8Bit, false, stereo, true);
                startDmaScheduler = true;
                break;
            }

            case Commands.ExitAutoInit8:
                _dmaState.AutoInit = false;
                _dmaState.AutoSizeBytes = 0;
                break;

            case Commands.SingleCycleDmaOutput16:
            case Commands.SingleCycleDmaOutput16Fifo: {
                bool stereo = (_commandData[0] & (1 << 5)) != 0;
                BeginDmaPlayback(DmaPlaybackMode.Pcm16Bit, true, stereo, false);
                startDmaScheduler = true;
                break;
            }

            case Commands.AutoInitDmaOutput16:
            case Commands.AutoInitDmaOutput16Fifo: {
                bool stereo = (_commandData[0] & (1 << 5)) != 0;
                BeginDmaPlayback(DmaPlaybackMode.Pcm16Bit, true, stereo, true);
                startDmaScheduler = true;
                break;
            }

            case Commands.TurnOnSpeaker:
                SetSpeakerEnabled(true);
                break;

            case Commands.TurnOffSpeaker:
                SetSpeakerEnabled(false);
                break;

            case Commands.PauseDmaMode:
            case Commands.PauseDmaMode16:
            case Commands.ExitDmaMode16:
                _dsp.IsDmaTransferActive = false;
                stopDmaScheduler = true;
                break;

            case Commands.ContinueDmaMode:
            case Commands.ContinueDmaMode16:
                _dsp.IsDmaTransferActive = true;
                startDmaScheduler = true;
                break;

            case Commands.RaiseIrq8:
                RaiseInterruptRequest(false);
                break;

            case Commands.SetInputSampleRate:
                // Ignore for now.
                break;

            case Commands.PauseForDuration:
                _pauseDuration = _commandData[0] | (_commandData[1] << 8);
                break;

            default:
                _loggerService.Warning("Sound Blaster command {CurrentCommand} not implemented", _currentCommand);

                return false;
        }

        if (stopDmaScheduler) {
            StopDmaScheduler();
        }

        if (startDmaScheduler) {
            StartDmaScheduler();
        }

        _blasterState = BlasterState.WaitingForCommand;
        return true;
    }

    private void RaiseInterruptRequest(bool is16Bit, bool ignoreDmaMask = false) {
        if (TryDeliverInterrupt(is16Bit, ignoreDmaMask)) {
            return;
        }

        _pendingIrq = true;
        _pendingIrqIs16Bit = is16Bit;
        _pendingIrqAllowMasked = ignoreDmaMask;
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            bool dmaMasked = !ignoreDmaMask && IsCurrentDmaMasked();
            bool irqMasked = IsIrqMasked();
            _loggerService.Verbose("Deferred Sound Blaster interrupt; DMA masked: {DmaMasked}, IRQ masked: {IrqMasked}",
                dmaMasked, irqMasked);
        }

        SchedulePendingIrqCheck();
    }

    /// <summary>
    ///     Resets the DSP.
    /// </summary>
    private void Reset() {
        _outputData.Clear();
        _outputData.Enqueue(0xAA);
        _blasterState = BlasterState.WaitingForCommand;
        _blockTransferSizeSet = false;
        StopDmaScheduler();
        ResetDmaState();
        _dsp.Reset();
        _resetCount++;
        CancelPendingIrqCheck();
        _pendingIrq = false;

        _dmaState.SpeakerEnabled = HasSpeaker;
        _dmaState.WarmupRemainingFrames = _dmaState.SpeakerEnabled ? _dmaState.ColdWarmupFrames : 0;
    }

    private bool TryDeliverInterrupt(bool is16Bit, bool ignoreDmaMask) {
        bool canDeliver = EvaluateInterruptStatus(ignoreDmaMask);
        if (!canDeliver) {
            return false;
        }

        DeliverInterrupt(is16Bit);
        return true;
    }

    private void DeliverInterrupt(bool is16Bit) {
        CancelPendingIrqCheck();
        _pendingIrq = false;
        _pendingIrqIs16Bit = false;
        _pendingIrqAllowMasked = false;
        _ctMixer.InterruptStatusRegister = is16Bit ? InterruptStatus.Dma16 : InterruptStatus.Dma8;
        _dualPic.ProcessInterruptRequest(IRQ);
    }

    private void TryDeliverPendingInterrupt() {
        if (!_pendingIrq) {
            CancelPendingIrqCheck();
            return;
        }

        if (TryDeliverInterrupt(_pendingIrqIs16Bit, _pendingIrqAllowMasked)) {
            return;
        }

        SchedulePendingIrqCheck();
    }

    private void SchedulePendingIrqCheck() {
        if (_pendingIrqEventScheduled || !_pendingIrq) {
            return;
        }

        _dualPic.AddEvent(_pendingIrqEventHandler, PendingIrqRetryIntervalMs);
        _pendingIrqEventScheduled = true;
    }

    private void CancelPendingIrqCheck() {
        if (!_pendingIrqEventScheduled) {
            return;
        }

        _dualPic.RemoveEvents(_pendingIrqEventHandler);
        _pendingIrqEventScheduled = false;
    }

    private void PendingIrqEvent(uint _) {
        _pendingIrqEventScheduled = false;
        TryDeliverPendingInterrupt();
    }

    private void OnPicIrqMaskChanged(byte irq, bool masked) {
        if (irq != _config.Irq) {
            return;
        }

        if (!masked) {
            TryDeliverPendingInterrupt();
        }
    }

    private bool EvaluateInterruptStatus(bool ignoreDmaMask) {
        if (!ignoreDmaMask && IsCurrentDmaMasked()) {
            return false;
        }

        return !IsIrqMasked();
    }

    private bool IsCurrentDmaMasked() {
        DmaChannel? channel = _dsp.CurrentChannel;
        return channel is not null && channel.IsMasked;
    }

    private bool IsIrqMasked() {
        DualPic.PicChannel channel = _config.Irq < 8 ? DualPic.PicChannel.Primary : DualPic.PicChannel.Secondary;
        PicChannelSnapshot snapshot = _dualPic.GetChannelSnapshot(channel);
        int bit = _config.Irq < 8 ? _config.Irq : _config.Irq - 8;
        return (snapshot.InterruptMaskRegister & (1 << bit)) != 0;
    }

    private struct BufferStatus() {
        // Bits 0-6: Reserved (always set to 1 when reading)
        public byte Reserved {
            get => (byte)(Data & 0b0111_1111);
            set => Data = (byte)((Data & 0b1000_0000) | (value & 0b0111_1111));
        }

        // Bit 7: HasData
        public bool HasData {
            set {
                if (value) {
                    Data |= 0b1000_0000;
                } else {
                    Data &= 0b0111_1111;
                }
            }
        }

        public byte Data { get; private set; } = 0b1111_1111;
    }

    private enum DmaPlaybackMode {
        None,
        Pcm8Bit,
        Pcm16Bit,
        Pcm16BitAliased,
        Adpcm2Bit,
        Adpcm3Bit,
        Adpcm4Bit
    }

    private sealed class DmaPlaybackState {
        public DmaPlaybackMode Mode { get; set; } = DmaPlaybackMode.None;
        public bool AutoInit { get; set; }
        public bool Stereo { get; set; }
        public double RateBytesPerSecond { get; set; }
        public uint MinChunkBytes { get; set; }
        public uint RemainingBytes { get; set; }
        public uint AutoSizeBytes { get; set; }
        public bool IrqRaisedForCurrentBlock { get; set; }
        public bool DmaMasked { get; set; }
        public bool SpeakerEnabled { get; set; }
        public int WarmupRemainingFrames { get; set; }
        public int ColdWarmupFrames { get; set; }
        public int HotWarmupFrames { get; set; }
        public double LastPumpTimeMs { get; set; }
    }
}
