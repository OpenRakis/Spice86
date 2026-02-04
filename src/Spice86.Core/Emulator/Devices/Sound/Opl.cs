namespace Spice86.Core.Emulator.Devices.Sound;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.VM.Clock;
using Spice86.Core.Emulator.VM.EmulationLoopScheduler;
using Spice86.Libs.Sound.Common;
using Spice86.Libs.Sound.Devices.AdlibGold;
using Spice86.Libs.Sound.Devices.NukedOpl3;
using Spice86.Shared.Interfaces;

using System.Threading;

/// <summary>
///     Virtual device which emulates OPL FM sound.
///     Reference: DOSBox-staging src/hardware/audio/opl.cpp
/// </summary>
public class Opl : DefaultIOPortHandler, IDisposable {
    private const int OplSampleRateHz = 49716;

    private readonly AdlibGold? _adlibGold;
    private readonly Opl3Chip _chip = new();
    private readonly Lock _chipLock = new();
    private readonly EmulationLoopScheduler _scheduler;
    private readonly IEmulatedClock _clock;
    private readonly DualPic _dualPic;
    private readonly byte _oplIrqLine;
    private readonly OplMode _mode;

    // Two timer chips for DualOpl2 mode or single chip for other modes
    // Reference: OplChip chip[2] in DOSBox
    private readonly OplTimerChip[] _timerChips = [new OplTimerChip(), new OplTimerChip()];

    // FIFO queue for cycle-accurate OPL frame generation
    // Reference: std::queue<AudioFrame> fifo in DOSBox opl.h
    private readonly Queue<AudioFrame> _fifo = new();

    // Register cache for two chips (512 bytes)
    // Reference: OplRegisterCache cache[512] in DOSBox
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
    // Reference: opl.newm in DOSBox
    private byte _newMode;

    // Last selected address in the chip
    // Reference: union { uint16_t normal; uint8_t dual[2]; } reg in DOSBox
    private ushort _selectedRegister;
    private readonly byte[] _selectedRegisterDual = new byte[2];

    // AdLib Gold control state
    // Reference: struct ctrl in DOSBox
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
    /// <param name="dualPic">The dual PIC.</param>
    /// <param name="mode">OPL synthesis mode.</param>
    /// <param name="sbBase">Sound Blaster base I/O address for port registration.</param>
    /// <param name="enableOplIrq">True to forward OPL IRQs to the PIC.</param>
    /// <param name="oplIrqLine">IRQ line used when OPL IRQs are enabled.</param>
    /// <param name="mixerEnabled">True if SB mixer controls OPL volume.</param>
    public Opl(Mixer mixer, State state,
        IOPortDispatcher ioPortDispatcher, bool failOnUnhandledPort,
        ILoggerService loggerService, EmulationLoopScheduler scheduler, IEmulatedClock clock, DualPic dualPic,
        OplMode mode = OplMode.Opl3, ushort sbBase = 0x220, bool enableOplIrq = false, byte oplIrqLine = 5,
        bool mixerEnabled = false)
        : base(state, failOnUnhandledPort, loggerService) {
        mixer.LockMixerThread();

        _mode = mode;
        _sbBase = sbBase;
        _scheduler = scheduler;
        _clock = clock;
        _dualPic = dualPic;
        _useOplIrq = enableOplIrq;
        _oplIrqLine = oplIrqLine;
        _ctrlMixerEnabled = mixerEnabled;

        // Build channel features based on mode
        // Reference: DOSBox channel_features setup
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
        // Reference: adlib_gold = std::make_unique<AdlibGold>(OplSampleRateHz)
        if (_mode == OplMode.Opl3Gold) {
            _adlibGold = new AdlibGold(OplSampleRateHz, loggerService);
        }

        _loggerService.Debug(
            "Initializing OPL FM synth. Mode: {Mode}, Sample rate: {SampleRate}",
            _mode, OplSampleRateHz);

        // Volume gain matching DOSBox
        // Reference: constexpr auto OplVolumeGain = 1.5f
        const float OplVolumeGain = 1.5f;
        _mixerChannel.Set0dbScalar(OplVolumeGain);

        // Noise gate configuration matching DOSBox
        // Reference: threshold_db = -65.0f + gain_to_decibel(OplVolumeGain)
        float thresholdDb = -65.0f + GainToDecibel(OplVolumeGain);
        const float AttackTimeMs = 1.0f;
        const float ReleaseTimeMs = 100.0f;
        _mixerChannel.ConfigureNoiseGate(thresholdDb, AttackTimeMs, ReleaseTimeMs);
        _mixerChannel.EnableNoiseGate(true);

        const double MillisInSecond = 1000.0;
        _msPerFrame = MillisInSecond / OplSampleRateHz;
        _lastRenderedMs = _clock.ElapsedTimeMs;

        // Initialize chip
        Init();

        // Register I/O port handlers
        InitPortHandlers(ioPortDispatcher);

        mixer.UnlockMixerThread();
    }

    /// <summary>
    ///     Renders OPL frames up to the current emulated time, queueing them in the FIFO.
    ///     Reference: Opl::RenderUpToNow() in DOSBox opl.cpp
    /// </summary>
    private void RenderUpToNow() {
        double now = _clock.ElapsedTimeMs;
        // Wake up the channel and update the last rendered time datum.
        // assert(channel);
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

    /// <summary>
    ///     Initializes the OPL chip state.
    ///     Reference: Opl::Init() in DOSBox
    /// </summary>
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
                // Reference: WriteReg(0x105, 1); CacheWrite(0x105, 1);
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
    ///     Reference: initialize_opl_tone_generators() in DOSBox
    /// </summary>
    private void InitializeToneGenerators() {
        // The first 9 operators are used for 4-op FM synthesis.
        int[] fourOp = [0, 1, 2, 6, 7, 8, 12, 13, 14];
        foreach (int index in fourOp) {
            Opl3Operator slot = _chip.Slots[index];
            slot.EnvelopeGeneratorOutput = 511;
            slot.EnvelopeGeneratorLevel = 571;
            slot.EnvelopeGeneratorState = (byte)EnvelopeGeneratorStage.Sustain;
            slot.RegFrequencyMultiplier = 1;
            slot.RegKeyScaleLevel = 1;
            slot.RegTotalLevel = 15;
            slot.RegAttackRate = 15;
            slot.RegDecayRate = 1;
            slot.RegSustainLevel = 5;
            slot.RegReleaseRate = 3;
        }

        // The remaining 9 operators are used for 2-op FM synthesis.
        int[] twoOp = [3, 4, 5, 9, 10, 11, 15, 16, 17];
        foreach (int index in twoOp) {
            Opl3Operator slot = _chip.Slots[index];
            slot.EnvelopeGeneratorOutput = 511;
            slot.EnvelopeGeneratorLevel = 511;
            slot.EnvelopeGeneratorState = (byte)EnvelopeGeneratorStage.Sustain;
            slot.RegKeyScaleRate = 1;
            slot.RegFrequencyMultiplier = 1;
            slot.RegAttackRate = 15;
            slot.RegDecayRate = 2;
            slot.RegSustainLevel = 7;
            slot.RegReleaseRate = 4;
        }
    }

    /// <summary>
    ///     Registers this device for the appropriate I/O port ranges based on mode.
    ///     Reference: Port handler installation in DOSBox Opl constructor
    /// </summary>
    private void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
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

    /// <summary>
    ///     Releases resources held by the OPL FM device.
    /// </summary>
    private void Dispose(bool disposing) {
        if (_disposed) {
            return;
        }

        if (disposing) {
            if (_useOplIrq) {
                _dualPic.DeactivateIrq(_oplIrqLine);
            }

            _adlibGold?.Dispose();
        }

        _disposed = true;
    }

    /// <inheritdoc />
    /// <summary>
    ///     Reads from OPL I/O ports.
    ///     Reference: Opl::PortRead() in DOSBox
    /// </summary>
    public override byte ReadByte(ushort port) {
        lock (_chipLock) {
            if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                _loggerService.Verbose("OPL: ReadByte port=0x{Port:X4} mode={Mode}", port, _mode);
            }
            return PortRead(port);
        }
    }

    /// <summary>
    ///     Port read implementation matching DOSBox.
    ///     Reference: Opl::PortRead() in DOSBox
    /// </summary>
    private byte PortRead(ushort port) {
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("OPL: PortRead port=0x{Port:X4} mode={Mode}", port, _mode);
        }
        switch (_mode) {
            case OplMode.Opl2: {
                // Only respond on primary port, return 0xFF for others
                // Make sure the low bits are 6 on OPL2
                if ((port & 0x03) == 0) {
                    byte ret = (byte)(_timerChips[0].Read(_clock.ElapsedTimeMs) | 0x06);
                    if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                        _loggerService.Debug("OPL: PortRead offset=0x00 returning 0x{Ret:X2} (timer0 status | 0x06)", ret);
                    }
                    return ret;
                }
                byte retOpl2Default = 0xFF;
                if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                    _loggerService.Debug("OPL: PortRead offset not primary returning 0x{Ret:X2} (no device)", retOpl2Default);
                }
                return retOpl2Default;
            }

            case OplMode.DualOpl2: {
                // Only return for the lower ports
                if ((port & 0x01) != 0) {
                    byte ret = 0xFF;
                    if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                        _loggerService.Debug("OPL: PortRead DualOpl2 upper port returning 0x{Ret:X2} (no device)", ret);
                    }
                    return ret;
                }
                int timerIndex = (port >> 1) & 1;
                byte retTimer = (byte)(_timerChips[timerIndex].Read(_clock.ElapsedTimeMs) | 0x06);
                if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                    _loggerService.Debug("OPL: PortRead DualOpl2 returning 0x{Ret:X2} (timer chip {Index} status | 0x06)", retTimer, timerIndex);
                }
                return retTimer;
            }

            case OplMode.Opl3Gold: {
                if (_ctrlActive) {
                    if (port == 0x38A) {
                        // Control status, not busy
                        byte ret = 0;
                        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                            _loggerService.Debug("OPL: PortRead 0x38A returning 0x{Ret:X2} (AdlibGold control status: not busy)", ret);
                        }
                        return ret;
                    }
                    if (port == 0x38B) {
                        byte ret = AdlibGoldControlRead();
                        string desc = _ctrlIndex switch {
                            0x00 => "Board Options (0x50 expected)",
                            0x09 => "Left FM Volume (_ctrlLvol)",
                            0x0A => "Right FM Volume (_ctrlRvol)",
                            0x15 => "Audio Relocation (Cryo detection)",
                            _ => "AdlibGold control read"
                        };
                        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                            _loggerService.Debug("OPL: PortRead 0x38B returning 0x{Ret:X2} (AdlibGold index=0x{Idx:X2} => {Desc})", ret, _ctrlIndex, desc);
                        }
                        return ret;
                    }
                }
                goto case OplMode.Opl3;
            }

            case OplMode.Opl3: {
                // Return timer status only on base port
                if ((port & 0x03) == 0) {
                    byte ret = _timerChips[0].Read(_clock.ElapsedTimeMs);
                    if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                        _loggerService.Debug("OPL: PortRead Opl3 base port returning timer status 0x{Ret:X2}", ret);
                    }
                    return ret;
                }
                byte retOpl3Default = 0xFF;
                if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                    _loggerService.Debug("OPL: PortRead Opl3 non-base port returning 0x{Ret:X2} (no device)", retOpl3Default);
                }
                return retOpl3Default;
            }

            default: {
                byte ret = 0xFF;
                if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                    _loggerService.Debug("OPL: PortRead default returning 0x{Ret:X2} (unknown mode)", ret);
                }
                return ret;
            }
        }
    }

    /// <inheritdoc />
    /// <summary>
    ///     Writes to OPL I/O ports.
    ///     Reference: Opl::PortWrite() in DOSBox
    /// </summary>
    public override void WriteByte(ushort port, byte value) {
        lock (_chipLock) {
            if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                _loggerService.Verbose("OPL: WriteByte port=0x{Port:X4} value=0x{Value:X2} mode={Mode}", port, value, _mode);
            }
            RenderUpToNow();
            PortWrite(port, value);
        }
    }

    /// <summary>
    ///     Port write implementation matching DOSBox.
    ///     Reference: Opl::PortWrite() in DOSBox
    /// </summary>
    private void PortWrite(ushort port, byte value) {
        bool isDataPort = (port & 0x01) != 0;
        if (isDataPort) {
            // Data write
            switch (_mode) {
                case OplMode.Opl3Gold:
                    if (port == 0x38B && _ctrlActive) {
                        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
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
                            _loggerService.Debug("OPL: AdlibGold control write index=0x{Idx:X2} ({Desc}) value=0x{Value:X2}", _ctrlIndex, desc, value);
                        }
                        AdlibGoldControlWrite(value);
                        return;
                    }
                    goto case OplMode.Opl3;

                case OplMode.Opl2:
                case OplMode.Opl3:
                    if (!_timerChips[0].Write((byte)(_selectedRegister & 0xFF), value, _clock.ElapsedTimeMs)) {
                        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                            _loggerService.Debug("OPL: Data write to register 0x{Reg:X3} value=0x{Value:X2} (WriteReg)", _selectedRegister, value);
                        }
                        WriteReg(_selectedRegister, value);
                        CacheWrite(_selectedRegister, value);
                    }
                    break;

                case OplMode.DualOpl2:
                    // Not a 0x??8 port, then write to a specific port
                    if ((port & 0x08) == 0) {
                        int index = (port & 2) >> 1;
                        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                            _loggerService.Debug("OPL: Dual data write index={Index} reg=0x{Reg:X2} value=0x{Value:X2}", index, _selectedRegisterDual[index], value);
                        }
                        DualWrite((byte)index, _selectedRegisterDual[index], value);
                    } else {
                        // Write to both ports
                        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                            _loggerService.Debug("OPL: Dual data broadcast write reg0=0x{Reg0:X2} reg1=0x{Reg1:X2} value=0x{Value:X2}", _selectedRegisterDual[0], _selectedRegisterDual[1], value);
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
                    _selectedRegister = (ushort)(WriteAddr(port, value) & 0xFF);
                    if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                        _loggerService.Debug("OPL: Address write selected register set to 0x{Reg:X3} (Opl2)", _selectedRegister);
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
                            if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                                _loggerService.Debug("OPL: AdlibGold control index set to 0x{Idx:X2} ({Desc})", _ctrlIndex, idxDesc);
                            }
                            return;
                        }
                    }
                    goto case OplMode.Opl3;

                case OplMode.Opl3:
                    _selectedRegister = (ushort)(WriteAddr(port, value) & 0x1FF);
                    if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                        _loggerService.Debug("OPL: Address write selected register set to 0x{Reg:X3} (Opl3)", _selectedRegister);
                    }
                    break;
            }
        }
    }

    /// <summary>
    ///     Computes register address from port and value.
    ///     Reference: Opl::WriteAddr() in DOSBox
    /// </summary>
    private ushort WriteAddr(ushort port, byte value) {
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

    /// <summary>
    ///     Writes to OPL register.
    ///     Reference: Opl::WriteReg() in DOSBox
    /// </summary>
    private void WriteReg(ushort selectedReg, byte value) {
        _chip.WriteRegisterBuffered(selectedReg, value);
        if (selectedReg == 0x105) {
            _newMode = (byte)(value & 0x01);
        }
    }

    /// <summary>
    ///     Caches register write for capture support.
    ///     Reference: Opl::CacheWrite() in DOSBox
    /// </summary>
    private void CacheWrite(ushort port, byte value) {
        _registerCache[port & 0x1FF] = value;
    }

    /// <summary>
    ///     Writes to both OPL chips in DualOpl2 mode.
    ///     Reference: Opl::DualWrite() in DOSBox
    /// </summary>
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
        if (_timerChips[index].Write(reg, val, _clock.ElapsedTimeMs)) {
            return;
        }

        // Enabling panning
        if (reg >= 0xC0 && reg <= 0xC8) {
            val &= 0x0F;
            val |= (byte)(index != 0 ? 0xA0 : 0x50);
        }

        ushort fullPort = (ushort)(reg + (index != 0 ? 0x100 : 0));
        WriteReg(fullPort, val);
        CacheWrite(fullPort, val);
    }

    /// <summary>
    ///     AdLib Gold control register write.
    ///     Reference: Opl::AdlibGoldControlWrite() in DOSBox
    /// </summary>
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

    /// <summary>
    ///     Sets OPL volume from AdLib Gold control registers.
    /// </summary>
    private void SetVolume() {
        if (_ctrlMixerEnabled) {
            // Dune CD version uses 32 volume steps in an apparent mistake, should be 128
            float leftVol = (_ctrlLvol & 0x1F) / 31.0f;
            float rightVol = (_ctrlRvol & 0x1F) / 31.0f;
            _mixerChannel.SetAppVolume(new AudioFrame(leftVol, rightVol));
        }
    }

    /// <summary>
    ///     AdLib Gold control register read.
    ///     Reference: Opl::AdlibGoldControlRead() in DOSBox
    /// </summary>
    private byte AdlibGoldControlRead() {
        return _ctrlIndex switch {
            0x00 => 0x50, // Board Options: 16-bit ISA, surround module, no telephone/CDROM
            0x09 => _ctrlLvol, // Left FM Volume
            0x0A => _ctrlRvol, // Right FM Volume
            0x15 => 0x388 >> 3, // Audio Relocation - Cryo installer detection
            _ => 0xFF
        };
    }

    /// <inheritdoc />
    public override void WriteWord(ushort port, ushort value) {
        WriteByte(port, (byte)value);
        WriteByte((ushort)(port + 1), (byte)(value >> 8));
    }

    /// <summary>
    ///     OPL mixer callback - called by the mixer thread to generate frames.
    ///     Reference: Opl::AudioCallback() in DOSBox opl.cpp lines 433-458
    /// </summary>
    public void AudioCallback(int framesRequested) {
        int framesRemaining = framesRequested;
        lock (_chipLock) {
            Span<float> frameData = stackalloc float[2];
            // First, send any frames we've queued since the last callback
            while (framesRemaining > 0 && _fifo.Count > 0) {
                AudioFrame frame = _fifo.Dequeue();
                frameData[0] = frame.Left;
                frameData[1] = frame.Right;
                _mixerChannel.AddSamples_sfloat(1, frameData);
                framesRemaining--;
            }
            // If the queue's run dry, render the remainder and sync-up our time datum
            while (framesRemaining > 0) {
                AudioFrame frame = RenderFrame();
                frameData[0] = frame.Left;
                frameData[1] = frame.Right;
                _mixerChannel.AddSamples_sfloat(1, frameData);
                framesRemaining--;
            }
            // Update last rendered time to now (cycle-accurate sync)
            _lastRenderedMs = _clock.ElapsedTimeMs;
        }
    }

    /// <summary>
    ///     Renders a single OPL audio frame.
    ///     Reference: Opl::RenderFrame() in DOSBox
    /// </summary>
    private AudioFrame RenderFrame() {
        Span<short> buf = stackalloc short[2];
        _chip.GenerateStream(buf);

        if (_adlibGold is not null) {
            Span<float> floatBuf = stackalloc float[2];
            _adlibGold.Process(buf, 1, floatBuf);
            return new AudioFrame { Left = floatBuf[0], Right = floatBuf[1] };
        }

        return new AudioFrame { Left = buf[0], Right = buf[1] };
    }

    /// <summary>
    ///     OPL timer chip for handling timer registers and status.
    ///     Reference: OplChip class in DOSBox opl.h/opl.cpp
    /// </summary>
    private sealed class OplTimerChip {
        private readonly OplTimer _timer0 = new(80);  // 80 microseconds
        private readonly OplTimer _timer1 = new(320); // 320 microseconds

        /// <summary>
        ///     Handles timer register writes.
        ///     Reference: OplChip::Write() in DOSBox
        /// </summary>
        public bool Write(byte reg, byte value, double time) {
            switch (reg) {
                case 0x02:
                    _timer0.Update(time);
                    _timer0.SetCounter(value);
                    return true;

                case 0x03:
                    _timer1.Update(time);
                    _timer1.SetCounter(value);
                    return true;

                case 0x04:
                    // Reset overflow in both timers
                    if ((value & 0x80) != 0) {
                        _timer0.Reset();
                        _timer1.Reset();
                    } else {
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
        ///     Reference: OplChip::Read() in DOSBox
        /// </summary>
        public byte Read(double time) {
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
    ///     Reference: Timer class in DOSBox opl.h/opl.cpp
    /// </summary>
    private sealed class OplTimer {
        private readonly double _clockInterval;
        private double _start;
        private double _trigger;
        private double _counterInterval;
        private byte _counter;
        private bool _enabled;
        private bool _overflow;
        private bool _masked;

        public OplTimer(int micros) {
            _clockInterval = micros * 0.001; // interval in milliseconds
            SetCounter(0);
        }

        /// <summary>
        ///     Updates timer state and returns true if overflow occurred.
        ///     Reference: Timer::Update() in DOSBox
        /// </summary>
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

        /// <summary>
        ///     Resets overflow flag.
        ///     Reference: Timer::Reset() in DOSBox
        /// </summary>
        public void Reset() {
            _overflow = false;
        }

        /// <summary>
        ///     Sets timer counter value.
        ///     Reference: Timer::SetCounter() in DOSBox
        /// </summary>
        public void SetCounter(byte value) {
            _counter = value;
            _counterInterval = (256 - _counter) * _clockInterval;
        }

        /// <summary>
        ///     Sets timer mask.
        ///     Reference: Timer::SetMask() in DOSBox
        /// </summary>
        public void SetMask(bool set) {
            _masked = set;
            if (_masked) {
                _overflow = false;
            }
        }

        /// <summary>
        ///     Stops the timer.
        ///     Reference: Timer::Stop() in DOSBox
        /// </summary>
        public void Stop() {
            _enabled = false;
        }

        /// <summary>
        ///     Starts the timer.
        ///     Reference: Timer::Start() in DOSBox
        /// </summary>
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
