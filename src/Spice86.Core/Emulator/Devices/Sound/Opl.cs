namespace Spice86.Core.Emulator.Devices.Sound;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.VM.Clock;
using Spice86.Core.Emulator.VM.CpuSpeedLimit;
using Spice86.Core.Emulator.VM.EmulationLoopScheduler;
using Spice86.Libs.Sound.Common;
using Spice86.Libs.Sound.Devices.AdlibGold;
using Spice86.Libs.Sound.Devices.NukedOpl3;
using Spice86.Shared.Interfaces;

using System.Threading;

/// <summary>
/// Represents the SB OPL synthesizer chip for audio emulation, supporting multiple synthesis modes and providing methods
/// for rendering audio frames and handling I/O operations.
/// </summary>
/// <remarks>The Opl class supports OPL2, Dual OPL2, OPL3, and OPL3 Gold synthesis modes. It manages audio
/// rendering, integrates with a mixer for sound output, and handles I/O port registration based on the selected mode.
/// </remarks>
public class Opl : DefaultIOPortHandler, IDisposable {
    private const int OplSampleRateHz = 49716;

    private readonly AdlibGold? _adlibGold;
    private readonly ILoggerService _logger;
    private readonly Opl3Chip _chip = new();
    private readonly Lock _chipLock = new();
    private readonly EmulationLoopScheduler _scheduler;
    private readonly IEmulatedClock _clock;
    private readonly ICyclesLimiter _cyclesLimiter;
    private readonly DualPic _dualPic;
    private readonly byte _oplIrqLine;
    private readonly OplMode _mode;

    // Two timer chips for DualOpl2 mode or single chip for other modes
    private readonly OplChip[] _timerChips;

    // FIFO queue for cycle-accurate OPL frame generation
    private readonly Queue<AudioFrame> _fifo = new();

    // Register cache for two chips (512 bytes)
    private readonly byte[] _registerCache = new byte[512];

    // Time tracking for cycle-accurate rendering
    private double _lastRenderedMs;
    private readonly double _msPerFrame;

    /// <summary>
    ///     The mixer channel used for the OPL synth.
    /// </summary>
    private readonly MixerChannel _mixerChannel;

    private readonly bool _useOplIrq;
    private bool _disposed;

    // OPL3 new mode flag
    private byte _newMode;

    // Last selected address in the chip
    private ushort _selectedRegister;
    private readonly byte[] _selectedRegisterDual = new byte[2];

    // AdLib Gold control state
    private byte _ctrlIndex;
    private byte _ctrlLvol = 0xFF;
    private byte _ctrlRvol = 0xFF;
    private bool _ctrlActive;
    private readonly bool _ctrlMixerEnabled;

    // Sound Blaster base address for port registration
    private readonly ushort _sbBase;

    /// <summary>
    ///     Initializes a new instance of the OPL synth chip.
    /// </summary>
    /// <param name="mixer">The global software mixer.</param>
    /// <param name="state">The CPU registers and flags.</param>
    /// <param name="ioPortDispatcher">I/O port dispatcher.</param>
    /// <param name="failOnUnhandledPort">Whether to throw on unhandled port access.</param>
    /// <param name="loggerService">The logger service.</param>
    /// <param name="scheduler">The event scheduler.</param>
    /// <param name="clock">The emulated clock.</param>
    /// <param name="cyclesLimiter">The CPU cycle limiter, used for I/O port read delay simulation.</param>
    /// <param name="dualPic">The dual PIC.</param>
    /// <param name="mode">OPL synthesis mode.</param>
    /// <param name="sbBase">Sound Blaster base I/O address for port registration.</param>
    /// <param name="enableOplIrq">True to forward OPL IRQs to the PIC.</param>
    /// <param name="oplIrqLine">IRQ line used when OPL IRQs are enabled.</param>
    /// <param name="mixerEnabled">True if SB mixer controls OPL volume.</param>
    public Opl(Mixer mixer, State state,
        IOPortDispatcher ioPortDispatcher, bool failOnUnhandledPort,
        ILoggerService loggerService, EmulationLoopScheduler scheduler, IEmulatedClock clock,
        ICyclesLimiter cyclesLimiter, DualPic dualPic,
        OplMode mode = OplMode.Opl3, ushort sbBase = 0x220, bool enableOplIrq = false, byte oplIrqLine = 5,
        bool mixerEnabled = false)
        : base(state, failOnUnhandledPort, loggerService) {

        _logger = loggerService;

        mixer.LockMixerThread();

        _mode = mode;
        _sbBase = sbBase;
        _scheduler = scheduler;
        _clock = clock;
        _cyclesLimiter = cyclesLimiter;
        _timerChips = [new OplChip(clock), new OplChip(clock)];
        _dualPic = dualPic;
        _useOplIrq = enableOplIrq;
        _oplIrqLine = oplIrqLine;
        _ctrlMixerEnabled = mixerEnabled;

        // Build channel features based on mode
        HashSet<ChannelFeature> features = [
            ChannelFeature.Sleep,
            ChannelFeature.FadeOut,
            ChannelFeature.NoiseGate,
            ChannelFeature.ReverbSend,
            ChannelFeature.ChorusSend,
            ChannelFeature.Synthesizer
        ];

        // Stereo only for dual OPL configurations
        bool isDualOpl = _mode != OplMode.Opl2;
        if (isDualOpl) {
            features.Add(ChannelFeature.Stereo);
        }

        _mixerChannel = mixer.AddChannel(AudioCallback, OplSampleRateHz, nameof(Opl), features);
        _mixerChannel.SetResampleMethod(ResampleMethod.Resample);

        // Initialize AdLib Gold for Opl3Gold mode
        if (_mode == OplMode.Opl3Gold) {
            _adlibGold = new AdlibGold(OplSampleRateHz, loggerService);
        }

        if (_logger.IsEnabled(LogEventLevel.Information)) {
            _logger.Information(
                "OPL: Running {Mode} on ports {BasePort:X3}h and {AdLibPort:X3}h at {SampleRate} Hz",
                _mode, _sbBase, 0x388, OplSampleRateHz);
        }

        // Volume gain
        const float OplVolumeGain = 1.5f;
        _mixerChannel.Set0dbScalar(OplVolumeGain);

        // Noise gate configuration
        float thresholdDb = -65.0f + GainToDecibel(OplVolumeGain);
        const float AttackTimeMs = 1.0f;
        const float ReleaseTimeMs = 100.0f;
        _mixerChannel.ConfigureNoiseGate(thresholdDb, AttackTimeMs, ReleaseTimeMs);
        _mixerChannel.EnableNoiseGate(true);

        const double MillisInSecond = 1000.0;
        _msPerFrame = MillisInSecond / OplSampleRateHz;
        _lastRenderedMs = _clock.ElapsedTimeMs;
        Init();
        InitPortHandlers(ioPortDispatcher);
        mixer.UnlockMixerThread();
    }

    private void RenderUpToNow() {
        double now = _clock.ElapsedTimeMs;
        // Wake up the channel and update the last rendered time datum.
        if (_mixerChannel.WakeUp()) {
            _lastRenderedMs = now;
            return;
        }
        // Keep rendering until we're current
        while (_lastRenderedMs < now) {
            _lastRenderedMs += _msPerFrame;
            _fifo.Enqueue(RenderFrame());
        }
    }

    /// <summary>
    ///     Exposes the OPL mixer channel for other components (e.g., SoundBlaster hardware mixer).
    /// </summary>
    public MixerChannel MixerChannel => _mixerChannel;

    /// <summary>
    ///     Gets the current OPL synthesis mode.
    /// </summary>
    public OplMode Mode => _mode;

    /// <inheritdoc />
    public void Dispose() {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    ///     Gets a value indicating whether AdLib Gold processing is enabled.
    /// </summary>
    public bool IsAdlibGoldEnabled => _adlibGold is not null;

    /// <summary>
    ///     Converts linear gain to decibels.
    /// </summary>
    private static float GainToDecibel(float gain) {
        return 20.0f * MathF.Log10(gain);
    }

    private void Init() {
        _newMode = 0;
        _chip.Reset(OplSampleRateHz);
        InitializeToneGenerators();
        Array.Clear(_registerCache);

        switch (_mode) {
            case OplMode.Opl2:
                break;

            case OplMode.DualOpl2:
                // Set up OPL3 mode in the handler
                WriteReg(0x105, 1);
                CacheWrite(0x105, 1);
                break;

            case OplMode.Opl3:
                break;

            case OplMode.Opl3Gold:
                // AdLib Gold already initialized in constructor
                break;

            case OplMode.None:
                break;
        }
    }

    /// <summary>
    ///     Initializes default envelopes and rates for the OPL operators.
    /// </summary>
    /// <remarks>
    /// Initialize the OPL chip's 4-op and 2-op FM synthesis tone generators per the
    /// Adlib v1.51 driver's values. Games and audio players typically overwrite the
    /// card with their own settings however we know the following eight games by
    /// Silmarils rely on the card being initialized by the Adlib driver:
    ///
    /// - Boston Bomb Club (1991),
    /// - Bunny Bricks (1993),
    /// - Crystals of Arborea (1990),
    /// - Ishar 1 (1992),
    /// - Ishar 2 (1993),
    /// - Metal Mutant (1991),
    /// - Storm Master (1992), and
    /// - Transantarctica (1993).
    /// </remarks>
    private void InitializeToneGenerators() {
        // The first 9 operators are used for 4-op FM synthesis.
        int[] fourOp = [0, 1, 2, 6, 7, 8, 12, 13, 14];
        foreach (int index in fourOp) {
            Opl3Operator slot = _chip.Slots[index];
            slot.EnvelopeGeneratorOutput = 511;
            slot.EnvelopeGeneratorLevel = 571;
            slot.EnvelopeGeneratorState = (byte)EnvelopeGeneratorStage.Release;
            slot.RegFrequencyMultiplier = 1;
            slot.RegKeyScaleLevel = 1;
            slot.RegTotalLevel = 15;
            slot.RegAttackRate = 15;
            slot.RegDecayRate = 1;
            slot.RegSustainLevel = 5;
            slot.RegReleaseRate = 3;
            // all other non-pointer slot members are zero
        }

        // The remaining 9 operators are used for 2-op FM synthesis.
        int[] twoOp = [3, 4, 5, 9, 10, 11, 15, 16, 17];
        foreach (int index in twoOp) {
            Opl3Operator slot = _chip.Slots[index];
            slot.EnvelopeGeneratorOutput = 511;
            slot.EnvelopeGeneratorLevel = 511;
            slot.EnvelopeGeneratorState = (byte)EnvelopeGeneratorStage.Release;
            slot.RegKeyScaleRate = 1;
            slot.RegFrequencyMultiplier = 1;
            slot.RegAttackRate = 15;
            slot.RegDecayRate = 2;
            slot.RegSustainLevel = 7;
            slot.RegReleaseRate = 4;
            // all other non-pointer slot members are zero
        }
    }

    private void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
        // Don't register any ports when OPL is disabled
        if (_mode == OplMode.None) {
            return;
        }

        // 0x388-0x38b ports (AdLib base ports) - always registered
        ioPortDispatcher.AddIOPortHandler(0x388, this);
        ioPortDispatcher.AddIOPortHandler(0x389, this);
        ioPortDispatcher.AddIOPortHandler(0x38A, this);
        ioPortDispatcher.AddIOPortHandler(0x38B, this);

        // Sound Blaster base ports (0x220-0x223) for dual OPL modes
        bool isDualOpl = _mode != OplMode.Opl2;
        if (isDualOpl) {
            ioPortDispatcher.AddIOPortHandler(_sbBase, this);
            ioPortDispatcher.AddIOPortHandler((ushort)(_sbBase + 1), this);
            ioPortDispatcher.AddIOPortHandler((ushort)(_sbBase + 2), this);
            ioPortDispatcher.AddIOPortHandler((ushort)(_sbBase + 3), this);
        }

        // 0x228-0x229 ports (advanced OPL3 ports)
        ioPortDispatcher.AddIOPortHandler((ushort)(_sbBase + 8), this);
        ioPortDispatcher.AddIOPortHandler((ushort)(_sbBase + 9), this);
    }

    private void Dispose(bool disposing) {
        if (_disposed) {
            return;
        }

        if (disposing) {
            if (_logger.IsEnabled(LogEventLevel.Information)) {
                _logger.Information("OPL: Shutting down {Mode}", _mode);
            }

            if (_useOplIrq) {
                _dualPic.DeactivateIrq(_oplIrqLine);
            }

            _adlibGold?.Dispose();
        }

        _disposed = true;
    }

    public override byte ReadByte(ushort port) {
        byte result = PortRead(port);
        if (_logger.IsEnabled(LogEventLevel.Verbose)) {
            _logger.Verbose("OPL: ReadByte port=0x{Port:X4} => 0x{Result:X2}", port, result);
        }
        return result;
    }

    private byte PortRead(ushort port) {
        switch (_mode) {
            case OplMode.Opl2:
                // We allocated 4 ports, so just return -1 for the higher ones.
                if ((port & 0x03) == 0) {
                    // Make sure the low bits are 6 on OPL2
                    return (byte)(_timerChips[0].Read() | 0x06);
                }
                return 0xFF;

            case OplMode.DualOpl2:
                // Only return for the lower ports
                if ((port & 0x01) != 0) {
                    return 0xFF;
                }
                // Make sure the low bits are 6 on OPL2
                return (byte)(_timerChips[(port >> 1) & 1].Read() | 0x06);

            case OplMode.Opl3Gold:
                if (_ctrlActive) {
                    if (port == 0x38A) {
                        // Control status, not busy
                        return 0;
                    }
                    if (port == 0x38B) {
                        return AdlibGoldControlRead();
                    }
                }
                goto case OplMode.Opl3;

            case OplMode.Opl3:
                // We allocated 4 ports, so just return -1 for the higher ones
                if ((port & 0x03) == 0) {
                    return _timerChips[0].Read();
                }
                return 0xFF;

            default:
                return 0xFF;
        }
    }

    public override void WriteByte(ushort port, byte value) {
        lock (_chipLock) {
            if (_logger.IsEnabled(LogEventLevel.Verbose)) {
                _logger.Verbose("OPL: WriteByte port=0x{Port:X4} value=0x{Value:X2} mode={Mode}", port, value, _mode);
            }
            if (!IsAdlibGoldControlWrite(port, value)) {
                RenderUpToNow();
            }
            PortWrite(port, value);
        }
    }

    private bool IsAdlibGoldControlWrite(ushort port, byte value) {
        if (_mode != OplMode.Opl3Gold) {
            return false;
        }

        if (port is not 0x38A and not 0x38B) {
            return false;
        }

        if (port == 0x38A && (value == 0xFF || value == 0xFE)) {
            return true;
        }

        if (_ctrlActive) {
            return true;
        }

        return false;
    }

    private void PortWrite(ushort port, byte value) {
        bool isDataPort = (port & 0x01) != 0;
        if (isDataPort) {
            // Data write
            switch (_mode) {
                case OplMode.Opl3Gold:
                    if (port == 0x38B && _ctrlActive) {
                        if (_logger.IsEnabled(LogEventLevel.Debug)) {
                            string desc = _ctrlIndex switch {
                                0x04 => "Stereo Volume Left",
                                0x05 => "Stereo Volume Right",
                                0x06 => "Bass",
                                0x07 => "Treble",
                                0x08 => "Switch Functions",
                                0x09 => "Left FM Volume",
                                0x0A => "Right FM Volume",
                                0x18 => "Surround Control",
                                _ => "AdlibGold control"
                            };
                            _logger.Debug("OPL: AdlibGold control write index=0x{Idx:X2} ({Desc}) value=0x{Value:X2}", _ctrlIndex, desc, value);
                        }
                        AdlibGoldControlWrite(value);
                        return;
                    }
                    goto case OplMode.Opl3;

                case OplMode.Opl2:
                case OplMode.Opl3:
                    if (!_timerChips[0].Write(_selectedRegister, value)) {
                        WriteReg(_selectedRegister, value);
                        CacheWrite(_selectedRegister, value);
                    }
                    break;

                case OplMode.DualOpl2:
                    // Not a 0x??8 port, then write to a specific port
                    if ((port & 0x08) == 0) {
                        int index = (port & 2) >> 1;
                        if (_logger.IsEnabled(LogEventLevel.Debug)) {
                            _logger.Debug("OPL: Dual data write index={Index} reg=0x{Reg:X2} value=0x{Value:X2}", index, _selectedRegisterDual[index], value);
                        }
                        DualWrite((byte)index, _selectedRegisterDual[index], value);
                    } else {
                        // Write to both ports
                        if (_logger.IsEnabled(LogEventLevel.Debug)) {
                            _logger.Debug("OPL: Dual data broadcast write reg0=0x{Reg0:X2} reg1=0x{Reg1:X2} value=0x{Value:X2}", _selectedRegisterDual[0], _selectedRegisterDual[1], value);
                        }
                        DualWrite(0, _selectedRegisterDual[0], value);
                        DualWrite(1, _selectedRegisterDual[1], value);
                    }
                    break;
            }
        } else {
            // Address write
            switch (_mode) {
                case OplMode.Opl2:
                    _selectedRegister = (ushort)(ComputeRegisterAddress(port, value) & 0xFF);
                    if (_logger.IsEnabled(LogEventLevel.Debug)) {
                        _logger.Debug("OPL: Address write selected register set to 0x{Reg:X3} (Opl2)", _selectedRegister);
                    }
                    break;

                case OplMode.DualOpl2:
                    // Not a 0x?88 port, write to a specific side
                    if ((port & 0x08) == 0) {
                        int index = (port & 2) >> 1;
                        _selectedRegisterDual[index] = (byte)(value & 0xFF);
                    } else {
                        _selectedRegisterDual[0] = (byte)(value & 0xFF);
                        _selectedRegisterDual[1] = (byte)(value & 0xFF);
                    }
                    break;

                case OplMode.Opl3Gold:
                    if (port == 0x38A) {
                        if (value == 0xFF) {
                            _ctrlActive = true;
                            return;
                        }
                        if (value == 0xFE) {
                            _ctrlActive = false;
                            return;
                        }
                        if (_ctrlActive) {
                            _ctrlIndex = value;
                            string idxDesc = GetAdlibGoldControlDescription(_ctrlIndex);
                            if (_logger.IsEnabled(LogEventLevel.Debug)) {
                                _logger.Debug("OPL: AdlibGold control index set to 0x{Idx:X2} ({Desc})", _ctrlIndex, idxDesc);
                            }
                            return;
                        }
                    }
                    goto case OplMode.Opl3;

                case OplMode.Opl3:
                    _selectedRegister = (ushort)(ComputeRegisterAddress(port, value) & 0x1FF);
                    if (_logger.IsEnabled(LogEventLevel.Debug)) {
                        _logger.Debug("OPL: Address write selected register set to 0x{Reg:X3} (Opl3)", _selectedRegister);
                    }
                    break;
            }
        }
    }

    private ushort ComputeRegisterAddress(ushort port, byte value) {
        ushort addr = value;
        // High bank if port bit 1 is set AND (writing to reg 0x05 OR newm is set)
        if ((port & 2) != 0 && (addr == 0x05 || _newMode != 0)) {
            addr |= 0x100;
        }
        return addr;
    }

    private static string GetAdlibGoldControlDescription(byte idx) {
        return idx switch {
            0x00 => "Board Options (0x50 expected)",
            0x09 => "Left FM Volume (_ctrlLvol)",
            0x0A => "Right FM Volume (_ctrlRvol)",
            0x15 => "Audio Relocation (Cryo detection)",
            _ => "AdlibGold control index"
        };
    }

    private void WriteReg(ushort selectedReg, byte value) {
        _chip.WriteRegisterBuffered(selectedReg, value);
        if (selectedReg == 0x105) {
            _newMode = (byte)(selectedReg & 0x01);
        }
    }

    private void CacheWrite(ushort port, byte value) {
        _registerCache[port & 0x1FF] = value;
    }

    private void DualWrite(byte index, byte reg, byte value) {
        // Make sure we don't use OPL3 features
        // Don't allow write to disable OPL3
        if (reg == 5) {
            return;
        }

        // Only allow 4 waveforms
        byte val = value;
        if (reg >= 0xE0) {
            val &= 3;
        }

        // Write to the timer?
        if (_timerChips[index].Write(reg, val)) {
            return;
        }

        // Enabling panning
        if (reg is >= 0xC0 and <= 0xC8) {
            val &= 0x0F;
            val |= (byte)(index != 0 ? 0xA0 : 0x50);
        }

        ushort fullPort = (ushort)(reg + (index != 0 ? 0x100 : 0));
        WriteReg(fullPort, val);
        CacheWrite(fullPort, val);
    }

    private void AdlibGoldControlWrite(byte value) {
        if (_adlibGold is null) {
            return;
        }

        switch (_ctrlIndex) {
            case 0x04:
                _adlibGold.StereoControlWrite(StereoProcessorControlReg.VolumeLeft, value);
                break;
            case 0x05:
                _adlibGold.StereoControlWrite(StereoProcessorControlReg.VolumeRight, value);
                break;
            case 0x06:
                _adlibGold.StereoControlWrite(StereoProcessorControlReg.Bass, value);
                break;
            case 0x07:
                _adlibGold.StereoControlWrite(StereoProcessorControlReg.Treble, value);
                break;
            case 0x08:
                _adlibGold.StereoControlWrite(StereoProcessorControlReg.SwitchFunctions, value);
                break;
            case 0x09: // Left FM Volume
                _ctrlLvol = value;
                SetVolume();
                break;
            case 0x0A: // Right FM Volume
                _ctrlRvol = value;
                SetVolume();
                break;
            case 0x18: // Surround
                _adlibGold.SurroundControlWrite(value);
                break;
        }
    }

    private void SetVolume() {
        if (_ctrlMixerEnabled) {
            // Dune CD version uses 32 volume steps in an apparent mistake, should be 128
            float leftVol = (_ctrlLvol & 0x1F) / 31.0f;
            float rightVol = (_ctrlRvol & 0x1F) / 31.0f;
            _mixerChannel.            AppVolume = new AudioFrame(leftVol, rightVol);
        }
    }

    private byte AdlibGoldControlRead() {
        return _ctrlIndex switch {
            0x00 => 0x50, // Board Options: 16-bit ISA, surround module, no telephone/CDROM
            0x09 => _ctrlLvol, // Left FM Volume
            0x0A => _ctrlRvol, // Right FM Volume
            0x15 => 0x388 >> 3, // Audio Relocation - Cryo installer detection
            _ => 0xFF
        };
    }

    public override void WriteWord(ushort port, ushort value) {
        WriteByte(port, (byte)value);
        WriteByte((ushort)(port + 1), (byte)(value >> 8));
    }

    public override ushort ReadWord(ushort port) {
        byte low = ReadByte(port);
        byte high = ReadByte((ushort)(port + 1));
        return (ushort)(low | (high << 8));
    }

    /// <summary>
    ///     OPL mixer callback - called by the mixer thread to generate frames.
    /// </summary>
    private void AudioCallback(int framesRequested) {
        int framesRemaining = framesRequested;
        lock (_chipLock) {
            Span<float> frameData = stackalloc float[2];
            // First, send any frames we've queued since the last callback
            while (framesRemaining > 0 && _fifo.Count > 0) {
                AudioFrame frame = _fifo.Dequeue();
                frameData[0] = frame.Left;
                frameData[1] = frame.Right;
                _mixerChannel.AddSamplesFloat(1, frameData);
                framesRemaining--;
            }
            // If the queue's run dry, render the remainder and sync-up our time datum
            while (framesRemaining > 0) {
                AudioFrame frame = RenderFrame();
                frameData[0] = frame.Left;
                frameData[1] = frame.Right;
                _mixerChannel.AddSamplesFloat(1, frameData);
                framesRemaining--;
            }
            // Update last rendered time to now using the atomic snapshot.
            // AudioCallback runs on the mixer thread, so we must use AtomicFullIndex
            // to avoid torn reads of the emulation thread's cycle state.
            _lastRenderedMs = _clock.ElapsedTimeMs;
        }
    }

    private AudioFrame RenderFrame() {
        Span<short> buf = stackalloc short[2];
        _chip.GenerateStream(buf);

        AudioFrame frame = new();
        if (_adlibGold is not null) {
            Span<float> floatBuf = stackalloc float[2];
            _adlibGold.Process(buf, 1, floatBuf);
            frame.Left = floatBuf[0];
            frame.Right = floatBuf[1];
        } else {
            frame.Left = buf[0];
            frame.Right = buf[1];
        }
        return frame;
    }

    private sealed class OplChip {
        private readonly Timer _timer0 = new(80);  // 80 microseconds
        private readonly Timer _timer1 = new(320); // 320 microseconds
        private readonly IEmulatedClock _clock;

        public OplChip(IEmulatedClock clock) {
            _clock = clock;
        }

        /// <summary>
        ///     Handles timer register writes.
        /// </summary>
        public bool Write(ushort reg, byte value) {
            switch (reg) {
                case 0x02:
                    _timer0.Update(_clock.ElapsedTimeMs);
                    _timer0.SetCounter(value);
                    return true;

                case 0x03:
                    _timer1.Update(_clock.ElapsedTimeMs);
                    _timer1.SetCounter(value);
                    return true;

                case 0x04:
                    // Reset overflow in both timers
                    if ((value & 0x80) != 0) {
                        _timer0.Reset();
                        _timer1.Reset();
                    } else {
                        double time = _clock.ElapsedTimeMs;

                        if ((value & 0x01) != 0) {
                            _timer0.Start(time);
                        } else {
                            _timer0.Stop();
                        }

                        if ((value & 0x02) != 0) {
                            _timer1.Start(time);
                        } else {
                            _timer1.Stop();
                        }

                        _timer0.SetMask((value & 0x40) != 0);
                        _timer1.SetMask((value & 0x20) != 0);
                    }
                    return true;

                default:
                    return false;
            }
        }

        /// <summary>
        ///     Reads timer status.
        /// </summary>
        public byte Read() {
            double time = _clock.ElapsedTimeMs;
            byte ret = 0;

            // Overflow won't be set if a channel is masked
            if (_timer0.Update(time)) {
                ret |= 0x40 | 0x80;
            }
            if (_timer1.Update(time)) {
                ret |= 0x20 | 0x80;
            }

            return ret;
        }
    }

    /// <summary>
    ///     Individual OPL timer.
    /// </summary>
    private sealed class Timer {
        private readonly double _clockInterval;
        private double _start;
        private double _trigger;
        private double _counterInterval;
        private byte _counter;
        private bool _enabled;
        private bool _overflow;
        private bool _masked;

        public Timer(int micros) {
            _clockInterval = micros * 0.001; // interval in milliseconds
            SetCounter(0);
        }

        public bool Update(double time) {
            if (_enabled && time >= _trigger) {
                // How far into the next cycle
                double deltaTime = time - _trigger;

                // Sync start to last cycle
                double counterMod = _counterInterval > 0 ? deltaTime % _counterInterval : 0;

                _start = time - counterMod;
                _trigger = _start + _counterInterval;

                // Only set the overflow flag when not masked
                if (!_masked) {
                    _overflow = true;
                }
            }
            return _overflow;
        }

        public void Reset() {
            // On a reset make sure the start is in sync with the next cycle
            _overflow = false;
        }

        public void SetCounter(byte value) {
            _counter = value;
            _counterInterval = (256 - _counter) * _clockInterval;
        }

        public void SetMask(bool set) {
            _masked = set;
            if (_masked) {
                _overflow = false;
            }
        }

        public void Stop() {
            _enabled = false;
        }

        public void Start(double time) {
            // Only properly start when not running before
            if (!_enabled) {
                _enabled = true;
                _overflow = false;

                // Sync start to the last clock interval
                double clockMod = _clockInterval > 0 ? time % _clockInterval : 0;

                _start = time - clockMod;

                // Overflow trigger
                _trigger = _start + _counterInterval;
            }
        }
    }
}
