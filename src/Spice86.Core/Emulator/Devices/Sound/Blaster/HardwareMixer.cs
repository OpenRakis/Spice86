namespace Spice86.Core.Emulator.Devices.Sound.Blaster;

using Serilog.Events;

using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Libs.Sound.Common;
using Spice86.Shared.Interfaces;

/// <summary>
/// Represents the SoundBlaster hardware mixer, also known as 'CTMixer'.
/// </summary>
public class HardwareMixer {
    private readonly SoundBlasterHardwareConfig _blasterHardwareConfig;
    private readonly ILoggerService _logger;
    private readonly MixerChannel _pcmMixerChannel;
    private readonly MixerChannel _OPLMixerChannel;

    // Mixer volume registers (0-31 range)
    private readonly byte[] _masterVolume = new byte[2] { 31, 31 }; // Left, Right
    private readonly byte[] _dacVolume = new byte[2] { 31, 31 };    // Left, Right
    private readonly byte[] _fmVolume = new byte[2] { 31, 31 };     // Left, Right
    private readonly byte[] _cdaVolume = new byte[2] { 31, 31 };    // Left, Right
    private readonly byte[] _lineVolume = new byte[2];   // Left, Right - zero-initialized like DOSBox
    private byte _micVolume; // zero-initialized like DOSBox

    // Sb16 advanced registers
    private byte _pcmLevel;
    private byte _recordingMonitor;
    private byte _recordingSource;
    private byte _recordingGain;
    private byte _recordingGainLeft;
    private byte _recordingGainRight;
    private byte _outputFilter;
    private byte _inputFilter;
    private byte _effects3D;
    private byte _altFeatureEnable1;
    private byte _altFeatureEnable2;
    private byte _altFeatureStatus;
    private byte _gamePortControl;
    private byte _volumeControlMode;
    private byte _reserved;

    private bool _stereoEnabled;
    private bool _filterEnabled = true;

    // Storage for unhandled registers (SBPro 0x0C, SB16 0x3B-0x47)
    // Reference: soundblaster.cpp sb.mixer.unhandled[]
    private readonly byte[] _unhandled = new byte[0x48];

    /// <summary>
    /// Initializes a new instance of the <see cref="HardwareMixer"/> class
    /// </summary>
    /// <param name="soundBlasterHardwareConfig">The SoundBlaster IRQs, and DMA information.</param>
    /// <param name="pcmMixerChannel">The mixer channel for PCM/DAC sound effects.</param>
    /// <param name="OPLMixerChannel">The mixer channel for FM synth music.</param>
    /// <param name="loggerService">The service used for logging.</param>
    /// <param name="onStereoChange">Callback invoked when stereo mode changes via mixer register 0x0E.</param>
    public HardwareMixer(SoundBlasterHardwareConfig soundBlasterHardwareConfig,
        MixerChannel pcmMixerChannel, MixerChannel OPLMixerChannel,
        ILoggerService loggerService, Action<bool> onStereoChange) {
        _logger = loggerService;
        _blasterHardwareConfig = soundBlasterHardwareConfig;
        _pcmMixerChannel = pcmMixerChannel;
        _OPLMixerChannel = OPLMixerChannel;
        _onStereoChange = onStereoChange;
    }

    private readonly Action<bool> _onStereoChange;

    /// <summary>
    /// Gets or sets the current mixer register in use.
    /// </summary>
    public int CurrentAddress { get; set; }

    /// <summary>
    /// Gets or sets the interrupt status register for the mixer.
    /// </summary>
    public InterruptStatus InterruptStatusRegister { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether stereo output is enabled.
    /// </summary>
    public bool StereoEnabled {
        get => _stereoEnabled;
        private set {
            _stereoEnabled = value;
            UpdateMixerVolumes();
        }
    }

    /// <summary>
    /// Reads data from the <see cref="CurrentAddress"/>
    /// </summary>
    /// <returns>The data read from the current in use mixer register.</returns>
    public byte ReadData() {
        string regName = GetMixerRegisterName((byte)CurrentAddress);
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Debug)) {
            _logger.Debug("HARDWARE_MIXER: ReadData from register 0x{Addr:X2} ({Name})", CurrentAddress, regName);
        }

        byte ret;
        switch (CurrentAddress) {
            case MixerRegisters.Reset:
                ret = 0x00;
                break;

            case MixerRegisters.InterruptStatus:
                // Reference: soundblaster.cpp ctmixer_read() case 0x82
                // SB16 always sets bit 5 (0x20)
                ret = (byte)((byte)InterruptStatusRegister |
                    (_blasterHardwareConfig.SbType == SbType.Sb16 ? 0x20 : 0));
                break;

            case MixerRegisters.IRQ:
                return GetIRQByte();

            case MixerRegisters.DMA:
                return GetDMAByte();

            // Master Volume (SB2 only, mono)
            // Reference: soundblaster.cpp ctmixer_read() case 0x02
            case MixerRegisters.MasterVolumeSb2:
                return (byte)((_masterVolume[1] >> 1) & 0x0E);

            // Master Volume (SB Pro)
            case MixerRegisters.MasterVolume:
                return ReadStereoVolume(_masterVolume);

            // Voice/DAC Volume (SB Pro)
            case MixerRegisters.DacVolume:
                return ReadStereoVolume(_dacVolume);

            // FM Volume (SB2 only) + FM output selection
            // Reference: soundblaster.cpp ctmixer_read() case 0x06
            case MixerRegisters.FmVolumeSb2:
                return (byte)((_fmVolume[1] >> 1) & 0x0E);

            // CD Audio Volume (SB2 only)
            // Reference: soundblaster.cpp ctmixer_read() case 0x08
            case MixerRegisters.CdVolumeSb2:
                return (byte)((_cdaVolume[1] >> 1) & 0x0E);

            // FM Volume (SB Pro)
            case MixerRegisters.FmVolume:
                return ReadStereoVolume(_fmVolume);

            // CD Audio Volume (SB Pro)
            case MixerRegisters.CdVolume:
                return ReadStereoVolume(_cdaVolume);

            // Line-in Volume (SB Pro)
            case MixerRegisters.LineVolume:
                return ReadStereoVolume(_lineVolume);

            // Mic Level (SB Pro) or DAC Volume (SB2)
            // Reference: soundblaster.cpp ctmixer_read() case 0x0a
            case MixerRegisters.MicVolume:
                if (_blasterHardwareConfig.SbType == SbType.SB2) {
                    return (byte)(_dacVolume[0] >> 2);
                }
                return (byte)((_micVolume >> 2) & (_blasterHardwareConfig.SbType == SbType.Sb16 ? 0x07 : 0x06));

            // Input Control (SBPro only, stored as unhandled)
            case MixerRegisters.InputControl:
                if (_blasterHardwareConfig.SbType == SbType.SBPro1 || _blasterHardwareConfig.SbType == SbType.SBPro2) {
                    return _unhandled[MixerRegisters.InputControl];
                }
                ret = 0x0A;
                break;

            // Output/Stereo Select
            case MixerRegisters.OutputStereoSelect:
                return (byte)(0x11 | (_stereoEnabled ? 0x02 : 0x00) | (_filterEnabled ? 0x00 : 0x20));

            // Sb16-specific registers
            case MixerRegisters.MasterVolumeLeft:
                if (_blasterHardwareConfig.SbType == SbType.Sb16) {
                    return (byte)(_masterVolume[0] << 3);
                }
                return 0x0A;

            case MixerRegisters.MasterVolumeRight:
                if (_blasterHardwareConfig.SbType == SbType.Sb16) {
                    return (byte)(_masterVolume[1] << 3);
                }
                return 0x0A;

            case MixerRegisters.DacVolumeLeft:
                if (_blasterHardwareConfig.SbType == SbType.Sb16) {
                    return (byte)(_dacVolume[0] << 3);
                }
                return 0x0A;

            case MixerRegisters.DacVolumeRight:
                if (_blasterHardwareConfig.SbType == SbType.Sb16) {
                    return (byte)(_dacVolume[1] << 3);
                }
                return 0x0A;

            case MixerRegisters.FmVolumeLeft:
                if (_blasterHardwareConfig.SbType == SbType.Sb16) {
                    return (byte)(_fmVolume[0] << 3);
                }
                return 0x0A;

            case MixerRegisters.FmVolumeRight:
                if (_blasterHardwareConfig.SbType == SbType.Sb16) {
                    return (byte)(_fmVolume[1] << 3);
                }
                return 0x0A;

            case MixerRegisters.CdVolumeLeft:
                if (_blasterHardwareConfig.SbType == SbType.Sb16) {
                    return (byte)(_cdaVolume[0] << 3);
                }
                return 0x0A;

            case MixerRegisters.CdVolumeRight:
                if (_blasterHardwareConfig.SbType == SbType.Sb16) {
                    return (byte)(_cdaVolume[1] << 3);
                }
                return 0x0A;

            case MixerRegisters.LineVolumeLeft:
                if (_blasterHardwareConfig.SbType == SbType.Sb16) {
                    return (byte)(_lineVolume[0] << 3);
                }
                return 0x0A;

            case MixerRegisters.LineVolumeRight:
                if (_blasterHardwareConfig.SbType == SbType.Sb16) {
                    return (byte)(_lineVolume[1] << 3);
                }
                return 0x0A;

            case MixerRegisters.MicVolumeSb16:
                if (_blasterHardwareConfig.SbType == SbType.Sb16) {
                    return (byte)(_micVolume << 3);
                }
                return 0x0A;

            // Sb16 advanced registers
            case MixerRegisters.Sb16PcmLevel:
                return _blasterHardwareConfig.SbType == SbType.Sb16 ? _pcmLevel : (byte)0;

            case MixerRegisters.Sb16RecordingMonitor:
                return _blasterHardwareConfig.SbType == SbType.Sb16 ? _recordingMonitor : (byte)0;

            case MixerRegisters.Sb16RecordingSource:
                return _blasterHardwareConfig.SbType == SbType.Sb16 ? _recordingSource : (byte)0;

            case MixerRegisters.Sb16RecordingGain:
                return _blasterHardwareConfig.SbType == SbType.Sb16 ? _recordingGain : (byte)0;

            case MixerRegisters.Sb16RecordingGainLeft:
                return _blasterHardwareConfig.SbType == SbType.Sb16 ? _recordingGainLeft : (byte)0;

            case MixerRegisters.Sb16RecordingGainRight:
                return _blasterHardwareConfig.SbType == SbType.Sb16 ? _recordingGainRight : (byte)0;

            case MixerRegisters.Sb16OutputFilter:
                return _blasterHardwareConfig.SbType == SbType.Sb16 ? _outputFilter : (byte)0;

            case MixerRegisters.Sb16InputFilter:
                return _blasterHardwareConfig.SbType == SbType.Sb16 ? _inputFilter : (byte)0;

            case MixerRegisters.Sb16Effects3D:
                return _blasterHardwareConfig.SbType == SbType.Sb16 ? _effects3D : (byte)0;

            case MixerRegisters.Sb16AltFeatureEnable1:
                return _blasterHardwareConfig.SbType == SbType.Sb16 ? _altFeatureEnable1 : (byte)0;

            case MixerRegisters.Sb16AltFeatureEnable2:
                return _blasterHardwareConfig.SbType == SbType.Sb16 ? _altFeatureEnable2 : (byte)0;

            case MixerRegisters.Sb16AltFeatureStatus:
                return _blasterHardwareConfig.SbType == SbType.Sb16 ? _altFeatureStatus : (byte)0;

            case MixerRegisters.Sb16GamePortControl:
                return _blasterHardwareConfig.SbType == SbType.Sb16 ? _gamePortControl : (byte)0;

            case MixerRegisters.Sb16VolumeControlMode:
                return _blasterHardwareConfig.SbType == SbType.Sb16 ? _volumeControlMode : (byte)0;

            case MixerRegisters.Sb16Reserved:
                return _blasterHardwareConfig.SbType == SbType.Sb16 ? _reserved : (byte)0;

            default:
                if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
                    _logger.Warning("Read from unsupported mixer register {CurrentAddress:X2}h", CurrentAddress);
                }
                // Reference: soundblaster.cpp ctmixer_read() default case returns 0x0A
                ret = 0x0A;
                break;
        }
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Debug)) {
            _logger.Debug("HARDWARE_MIXER: Read register 0x{Addr:X2} ({Name}) => 0x{Val:X2}", CurrentAddress, regName, ret);
        }

        return ret;
    }

    private static string GetMixerRegisterName(byte addr) {
        return addr switch {
            MixerRegisters.Reset => "Reset",
            MixerRegisters.MasterVolumeSb2 => "MasterVolumeSb2",
            MixerRegisters.InterruptStatus => "InterruptStatus",
            MixerRegisters.IRQ => "IRQ",
            MixerRegisters.DMA => "DMA",
            MixerRegisters.MasterVolume => "MasterVolume",
            MixerRegisters.DacVolume => "DacVolume",
            MixerRegisters.FmVolumeSb2 => "FmVolumeSb2",
            MixerRegisters.CdVolumeSb2 => "CdVolumeSb2",
            MixerRegisters.FmVolume => "FmVolume",
            MixerRegisters.CdVolume => "CdVolume",
            MixerRegisters.LineVolume => "LineVolume",
            MixerRegisters.MicVolume => "MicVolume",
            MixerRegisters.InputControl => "InputControl",
            MixerRegisters.OutputStereoSelect => "OutputStereoSelect",
            MixerRegisters.MasterVolumeLeft => "MasterVolumeLeft",
            MixerRegisters.MasterVolumeRight => "MasterVolumeRight",
            MixerRegisters.DacVolumeLeft => "DacVolumeLeft",
            MixerRegisters.DacVolumeRight => "DacVolumeRight",
            MixerRegisters.FmVolumeLeft => "FmVolumeLeft",
            MixerRegisters.FmVolumeRight => "FmVolumeRight",
            MixerRegisters.CdVolumeLeft => "CdVolumeLeft",
            MixerRegisters.CdVolumeRight => "CdVolumeRight",
            MixerRegisters.LineVolumeLeft => "LineVolumeLeft",
            MixerRegisters.LineVolumeRight => "LineVolumeRight",
            MixerRegisters.MicVolumeSb16 => "MicVolumeSb16",
            MixerRegisters.Sb16PcmLevel => "Sb16PcmLevel",
            MixerRegisters.Sb16RecordingMonitor => "Sb16RecordingMonitor",
            MixerRegisters.Sb16RecordingSource => "Sb16RecordingSource",
            MixerRegisters.Sb16RecordingGain => "Sb16RecordingGain",
            MixerRegisters.Sb16RecordingGainLeft => "Sb16RecordingGainLeft",
            MixerRegisters.Sb16RecordingGainRight => "Sb16RecordingGainRight",
            MixerRegisters.Sb16OutputFilter => "Sb16OutputFilter",
            MixerRegisters.Sb16InputFilter => "Sb16InputFilter",
            MixerRegisters.Sb16Effects3D => "Sb16Effects3D",
            MixerRegisters.Sb16AltFeatureEnable1 => "Sb16AltFeatureEnable1",
            MixerRegisters.Sb16AltFeatureEnable2 => "Sb16AltFeatureEnable2",
            MixerRegisters.Sb16AltFeatureStatus => "Sb16AltFeatureStatus",
            MixerRegisters.Sb16GamePortControl => "Sb16GamePortControl",
            MixerRegisters.Sb16VolumeControlMode => "Sb16VolumeControlMode",
            MixerRegisters.Sb16Reserved => "Sb16Reserved",
            _ => $"0x{addr:X2}"
        };
    }
    /// <summary>
    /// Write data to the <see cref="CurrentAddress"/> of the hardware mixer. <br/>
    /// For example, the FM volume register is written to when the address is <c>0x26</c>.
    /// </summary>
    /// <param name="value">The value to apply.</param>
    public void Write(byte value) {
        string regName = GetMixerRegisterName((byte)CurrentAddress);
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Debug)) {
            _logger.Debug("HARDWARE_MIXER: Write register 0x{Addr:X2} ({Name}) <= 0x{Val:X2}", CurrentAddress, regName, value);
        }

        switch (CurrentAddress) {
            case MixerRegisters.Reset:
                Reset();
                break;

            // Master Volume (SB2 only, mono)
            // Reference: soundblaster.cpp ctmixer_write() case 0x02
            case MixerRegisters.MasterVolumeSb2:
                WriteStereoVolume(_masterVolume, (byte)((value & 0x0F) | (value << 4)));
                UpdateMixerVolumes();
                break;

            case MixerRegisters.MasterVolume:
                WriteStereoVolume(_masterVolume, value);
                UpdateMixerVolumes();
                break;

            case MixerRegisters.DacVolume:
                WriteStereoVolume(_dacVolume, value);
                UpdateMixerVolumes();
                break;

            // FM Volume (SB2 only) + FM output selection
            // Reference: soundblaster.cpp ctmixer_write() case 0x06
            case MixerRegisters.FmVolumeSb2:
                WriteStereoVolume(_fmVolume, (byte)((value & 0x0F) | (value << 4)));
                UpdateMixerVolumes();
                break;

            // CD Audio Volume (SB2 only)
            // Reference: soundblaster.cpp ctmixer_write() case 0x08
            case MixerRegisters.CdVolumeSb2:
                WriteStereoVolume(_cdaVolume, (byte)((value & 0x0F) | (value << 4)));
                UpdateMixerVolumes();
                break;

            case MixerRegisters.FmVolume:
                WriteStereoVolume(_fmVolume, value);
                UpdateMixerVolumes();
                break;

            case MixerRegisters.CdVolume:
                WriteStereoVolume(_cdaVolume, value);
                // No CD support in the emulator
                break;

            case MixerRegisters.LineVolume:
                WriteStereoVolume(_lineVolume, value);
                // Line-in input connection is not emulated.
                break;

            // Mic Level (SB Pro) or DAC Volume (SB2)
            // Reference: soundblaster.cpp ctmixer_write() case 0x0a
            case MixerRegisters.MicVolume:
                if (_blasterHardwareConfig.SbType == SbType.SB2) {
                    _dacVolume[0] = _dacVolume[1] = (byte)(((value & 0x06) << 2) | 3);
                    UpdateMixerVolumes();
                } else {
                    _micVolume = (byte)(((value & 0x07) << 2) |
                        (_blasterHardwareConfig.SbType == SbType.Sb16 ? 1 : 3));
                    // no microphone input support in the emulator
                }
                break;

            // Input Control (SBPro only)
            // Reference: soundblaster.cpp ctmixer_write() default case stores in unhandled[]
            case MixerRegisters.InputControl:
                if (_blasterHardwareConfig.SbType == SbType.SBPro1 || _blasterHardwareConfig.SbType == SbType.SBPro2) {
                    _unhandled[MixerRegisters.InputControl] = value;
                }
                break;

            // Output/Stereo Select
            // Reference: soundblaster.cpp ctmixer_write() case 0x0e
            case MixerRegisters.OutputStereoSelect: {
                bool newStereo = (value & 0x02) != 0;

                // Filter toggle is only possible on SBPro2
                // Reference: soundblaster.cpp "Toggling the filter programmatically is only possible on the Sound Blaster Pro 2."
                if (_blasterHardwareConfig.SbType == SbType.SBPro2) {
                    _filterEnabled = (value & 0x20) == 0;
                }

                // Invoke the stereo change callback (dsp_change_stereo equivalent)
                _onStereoChange(newStereo);

                _stereoEnabled = newStereo;
                UpdateMixerVolumes();

                if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Debug)) {
                    _logger.Debug("Mixer set to {StereoSetting} with filter {FilterSetting}",
                        _stereoEnabled ? "STEREO" : "MONO",
                        _filterEnabled ? "ENABLED" : "DISABLED");
                }
                break;
            }

            // Sb16-specific registers
            case MixerRegisters.MasterVolumeLeft:
                if (_blasterHardwareConfig.SbType == SbType.Sb16) {
                    _masterVolume[0] = (byte)(value >> 3);
                    UpdateMixerVolumes();
                }
                break;

            case MixerRegisters.MasterVolumeRight:
                if (_blasterHardwareConfig.SbType == SbType.Sb16) {
                    _masterVolume[1] = (byte)(value >> 3);
                    UpdateMixerVolumes();
                }
                break;

            case MixerRegisters.DacVolumeLeft:
                if (_blasterHardwareConfig.SbType == SbType.Sb16) {
                    _dacVolume[0] = (byte)(value >> 3);
                    UpdateMixerVolumes();
                }
                break;

            case MixerRegisters.DacVolumeRight:
                if (_blasterHardwareConfig.SbType == SbType.Sb16) {
                    _dacVolume[1] = (byte)(value >> 3);
                    UpdateMixerVolumes();
                }
                break;

            case MixerRegisters.FmVolumeLeft:
                if (_blasterHardwareConfig.SbType == SbType.Sb16) {
                    _fmVolume[0] = (byte)(value >> 3);
                    UpdateMixerVolumes();
                }
                break;

            case MixerRegisters.FmVolumeRight:
                if (_blasterHardwareConfig.SbType == SbType.Sb16) {
                    _fmVolume[1] = (byte)(value >> 3);
                    UpdateMixerVolumes();
                }
                break;

            case MixerRegisters.CdVolumeLeft:
                if (_blasterHardwareConfig.SbType == SbType.Sb16) {
                    _cdaVolume[0] = (byte)(value >> 3);
                    // No CD sound channel in current implementation of the software mixer,
                    // in fact - there is no CD support currently.
                }
                break;

            case MixerRegisters.CdVolumeRight:
                if (_blasterHardwareConfig.SbType == SbType.Sb16) {
                    _cdaVolume[1] = (byte)(value >> 3);
                    // No CD sound channel in current implementation of the software mixer,
                    // in fact - there is no CD support currently.
                }
                break;

            case MixerRegisters.LineVolumeLeft:
                if (_blasterHardwareConfig.SbType == SbType.Sb16) {
                    _lineVolume[0] = (byte)(value >> 3);
                    // Line-in input connection is not emulated.
                }
                break;

            case MixerRegisters.LineVolumeRight:
                if (_blasterHardwareConfig.SbType == SbType.Sb16) {
                    _lineVolume[1] = (byte)(value >> 3);
                    // Line-in input connection is not emulated.
                }
                break;

            case MixerRegisters.MicVolumeSb16:
                if (_blasterHardwareConfig.SbType == SbType.Sb16) {
                    _micVolume = (byte)(value >> 3);
                    // no microphone input support in the emulator
                }
                break;

            // Sb16 advanced registers
            case MixerRegisters.Sb16PcmLevel:
                if (_blasterHardwareConfig.SbType == SbType.Sb16) {
                    _pcmLevel = value;
                }
                break;

            case MixerRegisters.Sb16RecordingMonitor:
                if (_blasterHardwareConfig.SbType == SbType.Sb16) {
                    _recordingMonitor = value;
                }
                break;

            case MixerRegisters.Sb16RecordingSource:
                if (_blasterHardwareConfig.SbType == SbType.Sb16) {
                    _recordingSource = value;
                }
                break;

            case MixerRegisters.Sb16RecordingGain:
                if (_blasterHardwareConfig.SbType == SbType.Sb16) {
                    _recordingGain = value;
                }
                break;

            case MixerRegisters.Sb16RecordingGainLeft:
                if (_blasterHardwareConfig.SbType == SbType.Sb16) {
                    _recordingGainLeft = value;
                }
                break;

            case MixerRegisters.Sb16RecordingGainRight:
                if (_blasterHardwareConfig.SbType == SbType.Sb16) {
                    _recordingGainRight = value;
                }
                break;

            case MixerRegisters.Sb16OutputFilter:
                if (_blasterHardwareConfig.SbType == SbType.Sb16) {
                    _outputFilter = value;
                }
                break;

            case MixerRegisters.Sb16InputFilter:
                if (_blasterHardwareConfig.SbType == SbType.Sb16) {
                    _inputFilter = value;
                }
                break;

            case MixerRegisters.Sb16Effects3D:
                if (_blasterHardwareConfig.SbType == SbType.Sb16) {
                    _effects3D = value;
                }
                break;

            case MixerRegisters.Sb16AltFeatureEnable1:
                if (_blasterHardwareConfig.SbType == SbType.Sb16) {
                    _altFeatureEnable1 = value;
                }
                break;

            case MixerRegisters.Sb16AltFeatureEnable2:
                if (_blasterHardwareConfig.SbType == SbType.Sb16) {
                    _altFeatureEnable2 = value;
                }
                break;

            case MixerRegisters.Sb16AltFeatureStatus:
                if (_blasterHardwareConfig.SbType == SbType.Sb16) {
                    _altFeatureStatus = value;
                }
                break;

            case MixerRegisters.Sb16GamePortControl:
                if (_blasterHardwareConfig.SbType == SbType.Sb16) {
                    _gamePortControl = value;
                }
                break;

            case MixerRegisters.Sb16VolumeControlMode:
                if (_blasterHardwareConfig.SbType == SbType.Sb16) {
                    _volumeControlMode = value;
                }
                break;

            case MixerRegisters.Sb16Reserved:
                if (_blasterHardwareConfig.SbType == SbType.Sb16) {
                    _reserved = value;
                }
                break;

            default:
                if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
                    _logger.Warning("Write to unsupported mixer register {CurrentAddress:X2}h value {Value:X2}h", CurrentAddress, value);
                }
                break;
        }
    }

    /// <summary>
    /// Resets the mixer to its default state.
    /// Reference: soundblaster.cpp ctmixer_reset() — only resets fm, cda, dac, master to 31.
    /// Line-in, mic, stereo, filter, and SB16 advanced registers are NOT reset.
    /// </summary>
    public void Reset() {
        SetDefaultVolumes(_masterVolume);
        SetDefaultVolumes(_dacVolume);
        SetDefaultVolumes(_fmVolume);
        SetDefaultVolumes(_cdaVolume);

        UpdateMixerVolumes();

        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Debug)) {
            _logger.Debug("Sound Blaster mixer reset to defaults");
        }
    }

    private byte GetIRQByte() {
        return _blasterHardwareConfig.Irq switch {
            2 => 1 << 0,
            5 => 1 << 1,
            7 => 1 << 2,
            10 => 1 << 3,
            _ => 0,
        };
    }

    private byte GetDMAByte() {
        byte result = 0;

        // Low DMA channel
        switch (_blasterHardwareConfig.LowDma) {
            case 0: result |= 0x01; break;
            case 1: result |= 0x02; break;
            case 3: result |= 0x08; break;
        }

        // High DMA channel (Sb16)
        if (_blasterHardwareConfig.SbType == SbType.Sb16) {
            switch (_blasterHardwareConfig.HighDma) {
                case 5: result |= 0x20; break;
                case 6: result |= 0x40; break;
                case 7: result |= 0x80; break;
            }
        }

        return result;
    }

    private static void SetDefaultVolumes(byte[] volumes) {
        volumes[0] = 31;
        volumes[1] = 31;
    }

    private void WriteStereoVolume(byte[] target, byte value) {
        // SB Pro format: Left channel in high nibble, right channel in low nibble
        // Reference: soundblaster.cpp write_sb_pro_volume()
        // SB16 uses | 1, all other types use | 3
        int orMask = _blasterHardwareConfig.SbType == SbType.Sb16 ? 1 : 3;
        target[0] = (byte)(((value & 0xF0) >> 3) | orMask); // Left (bits 7-4)
        target[1] = (byte)(((value & 0x0F) << 1) | orMask); // Right (bits 3-0)
    }

    private byte ReadStereoVolume(byte[] source) {
        // Convert the internal 5-bit format back to the SB Pro mixer register format
        // Reference: soundblaster.cpp read_sb_pro_volume()
        // Only SBPro1/SBPro2 add | 0x11 to fill unused bits
        int orMask = (_blasterHardwareConfig.SbType == SbType.SBPro1 || _blasterHardwareConfig.SbType == SbType.SBPro2) ? 0x11 : 0;
        return (byte)(((source[0] & 0x1E) << 3) | ((source[1] & 0x1E) >> 1) | orMask);
    }

    private void UpdateMixerVolumes() {
        // Calculate the percentage for DAC volume
        float masterLeft = CalculatePercentage(_masterVolume[0]);
        float masterRight = CalculatePercentage(_masterVolume[1]);

        float dacLeft = CalculatePercentage(_dacVolume[0]) * masterLeft;
        float dacRight = CalculatePercentage(_dacVolume[1]) * masterRight;

        // Set app volume on the mixer channel (programmatic control from DOS software)
        _pcmMixerChannel.SetAppVolume(new AudioFrame(dacLeft, dacRight));

        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            _logger.Verbose("HARDWARE_MIXER: Updated DAC app volume L={Left:0.0000} R={Right:0.0000} (master L={ML} R={MR})",
                dacLeft, dacRight, _masterVolume[0], _masterVolume[1]);
        }

        float fmLeft = CalculatePercentage(_fmVolume[0]) * masterLeft;
        float fmRight = CalculatePercentage(_fmVolume[1]) * masterRight;

        // Set app volume on the mixer channel (programmatic control from DOS software)
        _OPLMixerChannel.SetAppVolume(new AudioFrame(fmLeft, fmRight));

        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            _logger.Verbose("HARDWARE_MIXER: Updated FM app volume L={Left:0.0000} R={Right:0.0000} (fm regs L={FL} R={FR})",
                fmLeft, fmRight, _fmVolume[0], _fmVolume[1]);
        }
    }

    private float CalculatePercentage(byte volume) {
        // The SB Pro volume values are attenuation values (31=max volume, 0=mute)
        // Reference: src/hardware/audio/soundblaster.cpp calc_vol()

        byte count = (byte)(31 - volume);
        float db = count;

        // Apply Sound Blaster type-specific adjustments
        if (_blasterHardwareConfig.SbType == SbType.SBPro1 || _blasterHardwareConfig.SbType == SbType.SBPro2) {
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
                    // Turn it off
                    return 0.0f;
                }
            }
        } else {
            // SB16 scale (and other SB types without specific data)
            db *= 2.0f;
            if (count > 20) {
                db -= 1.0f;
            }
        }

        // Convert dB to linear scale: 10^(-0.05 * db)
        // This is the inverse of the formula: dB = 20 * log10(linear)
        return (float)Math.Pow(10.0, -0.05 * db);
    }
}