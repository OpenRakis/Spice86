namespace Spice86.Core.Emulator.Devices.Sound.Blaster;

using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Shared.Interfaces;

/// <summary>
/// Represents the SoundBlaster hardware mixer, also known as 'CTMixer'.
/// </summary>
public class HardwareMixer {
    private readonly SoundBlasterHardwareConfig _blasterHardwareConfig;
    private readonly ILoggerService _logger;
    private readonly SoundChannel _pcmSoundChannel;
    private readonly SoundChannel _opl3fmSoundChannel;

    // Mixer volume registers (0-31 range)
    private readonly byte[] _masterVolume = new byte[2] { 31, 31 }; // Left, Right
    private readonly byte[] _dacVolume = new byte[2] { 31, 31 };    // Left, Right
    private readonly byte[] _fmVolume = new byte[2] { 31, 31 };     // Left, Right
    private readonly byte[] _cdaVolume = new byte[2] { 31, 31 };    // Left, Right
    private readonly byte[] _lineVolume = new byte[2] { 31, 31 };   // Left, Right
    private byte _micVolume = 31;
    
    // SB16 advanced registers
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

    /// <summary>
    /// Initializes a new instance of the <see cref="HardwareMixer"/> class
    /// </summary>
    /// <param name="soundBlasterHardwareConfig">The SoundBlaster IRQs, and DMA information.</param>
    /// <param name="pcmSoundChannel">The sound channel for sound effects.</param>
    /// <param name="opl3fmSoundChannel">The sound channel for FM synth music.</param>
    /// <param name="loggerService">The service used for logging.</param>
    public HardwareMixer(SoundBlasterHardwareConfig soundBlasterHardwareConfig,
        SoundChannel pcmSoundChannel, SoundChannel opl3fmSoundChannel,
        ILoggerService loggerService) {
        _logger = loggerService;
        _blasterHardwareConfig = soundBlasterHardwareConfig;
        _pcmSoundChannel = pcmSoundChannel;
        _opl3fmSoundChannel = opl3fmSoundChannel;
    }

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
        switch (CurrentAddress) {
            case MixerRegisters.Reset:
                return 0x00;

            case MixerRegisters.InterruptStatus:
                return (byte)InterruptStatusRegister;

            case MixerRegisters.IRQ:
                return GetIRQByte();

            case MixerRegisters.DMA:
                return GetDMAByte();

            // Master Volume (SB Pro)
            case MixerRegisters.MasterVolume:
                return ReadStereoVolume(_masterVolume);

            // Voice/DAC Volume (SB Pro)
            case MixerRegisters.DacVolume:
                return ReadStereoVolume(_dacVolume);

            // FM Volume (SB Pro)
            case MixerRegisters.FmVolume:
                return ReadStereoVolume(_fmVolume);

            // CD Audio Volume (SB Pro)
            case MixerRegisters.CdVolume:
                return ReadStereoVolume(_cdaVolume);

            // Line-in Volume (SB Pro)
            case MixerRegisters.LineVolume:
                return ReadStereoVolume(_lineVolume);

            // Mic Level (SB Pro)
            case MixerRegisters.MicVolume:
                return (byte)((_micVolume >> 2) & 0x07);

            // Output/Stereo Select
            case MixerRegisters.OutputStereoSelect:
                return (byte)(0x11 | (_stereoEnabled ? 0x02 : 0x00) | (_filterEnabled ? 0x00 : 0x20));

            // SB16-specific registers
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

            // SB16 advanced registers
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
                return 0x00;
        }
    }

    /// <summary>
    /// Write data to the <see cref="CurrentAddress"/> of the hardware mixer. <br/>
    /// For example, the FM volume register is written to when the address is <c>0x26</c>.
    /// </summary>
    /// <param name="value">The value to apply.</param>
    public void Write(byte value) {
        switch (CurrentAddress) {
            case MixerRegisters.Reset:
                Reset();
                break;

            case MixerRegisters.MasterVolume:
                WriteStereoVolume(_masterVolume, value);
                UpdateMixerVolumes();
                break;

            case MixerRegisters.DacVolume:
                WriteStereoVolume(_dacVolume, value);
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

            case MixerRegisters.MicVolume:
                _micVolume = (byte)(((value & 0x07) << 2) | 0x03);
                // no microphone input support in the emulator
                break;

            case MixerRegisters.OutputStereoSelect:
                StereoEnabled = (value & 0x02) != 0;
                _filterEnabled = (value & 0x20) == 0;

                if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Debug)) {
                    _logger.Debug("Mixer set to {StereoSetting} with filter {FilterSetting}",
                        StereoEnabled ? "STEREO" : "MONO",
                        _filterEnabled ? "ENABLED" : "DISABLED");
                }
                break;

            // SB16-specific registers
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

            // SB16 advanced registers
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
    /// </summary>
    public void Reset() {
        SetDefaultVolumes(_masterVolume);
        SetDefaultVolumes(_dacVolume);
        SetDefaultVolumes(_fmVolume);
        SetDefaultVolumes(_cdaVolume);
        SetDefaultVolumes(_lineVolume);
        _micVolume = 31;
        _stereoEnabled = false;
        _filterEnabled = true;

        // Reset SB16 advanced registers
        _pcmLevel = 0;
        _recordingMonitor = 0;
        _recordingSource = 0;
        _recordingGain = 0;
        _recordingGainLeft = 0;
        _recordingGainRight = 0;
        _outputFilter = 0;
        _inputFilter = 0;
        _effects3D = 0;
        _altFeatureEnable1 = 0;
        _altFeatureEnable2 = 0;
        _altFeatureStatus = 0;
        _gamePortControl = 0;
        _volumeControlMode = 0;
        _reserved = 0;

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

        // High DMA channel (SB16)
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

    private static void WriteStereoVolume(byte[] target, byte value) {
        // SB Pro format: Left channel in high nibble, right channel in low nibble
        target[0] = (byte)(((value & 0xF0) >> 3) | 0x03); // Left (bits 7-4)
        target[1] = (byte)(((value & 0x0F) << 1) | 0x03); // Right (bits 3-0)
    }

    private static byte ReadStereoVolume(byte[] source) {
        // Convert the internal 5-bit format back to the SB Pro mixer register format
        return (byte)(((source[0] & 0x1E) << 3) | ((source[1] & 0x1E) >> 1) | 0x11);
    }

    private void UpdateMixerVolumes() {
        // Calculate the percentage for DAC volume
        float masterLeft = CalculatePercentage(_masterVolume[0]);
        float masterRight = CalculatePercentage(_masterVolume[1]);

        float dacLeft = CalculatePercentage(_dacVolume[0]) * masterLeft;
        float dacRight = CalculatePercentage(_dacVolume[1]) * masterRight;
        _pcmSoundChannel.Volume = (int)((dacLeft + dacRight) * 50); // Average and scale to 0-100

        float fmLeft = CalculatePercentage(_fmVolume[0]) * masterLeft;
        float fmRight = CalculatePercentage(_fmVolume[1]) * masterRight;
        _opl3fmSoundChannel.Volume = (int)((fmLeft + fmRight) * 50); // Average and scale to 0-100
    }

    private static float CalculatePercentage(byte volume) {
        // The SB Pro volume values are attenuation values (31=max volume, 0=mute)
        if (volume >= 28) return 1.0f;  // Full volume
        if (volume <= 4) return 0.0f;   // Mute

        // Linear interpolation between 0.0 and 1.0
        return (volume - 4) / 24.0f;
    }
}