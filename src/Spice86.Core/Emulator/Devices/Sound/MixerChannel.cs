// SPDX-License-Identifier: GPL-2.0-or-later
// MixerChannel implementation mirrored from DOSBox Staging
// Reference: src/audio/mixer.h and mixer.cpp

namespace Spice86.Core.Emulator.Devices.Sound;

using Spice86.Core.Emulator.Devices.Sound.Blaster;
using Spice86.Libs.Sound.Common;
using Spice86.Libs.Sound.Filters.IirFilters.Filters.Butterworth;
using Spice86.Shared.Interfaces;

using System.Threading;

using HighPass = Spice86.Libs.Sound.Filters.IirFilters.Filters.Butterworth.HighPass;
using LowPass = Spice86.Libs.Sound.Filters.IirFilters.Filters.Butterworth.LowPass;

/// <summary>
/// Represents a single audio channel in the mixer.
/// Mirrors DOSBox Staging's MixerChannel class.
/// </summary>
public sealed class MixerChannel : IDisposable {
    private const uint SpeexChannels = 2; // Always use stereo for processing
    private const int SpeexQuality = 5; // Medium quality - good balance between CPU and quality
    
    // Envelope constants - mirrors DOSBox mixer.cpp lines 58-62
    private const byte EnvelopeMaxExpansionOverMs = 15; // Envelope expands over 15ms
    private const byte EnvelopeExpiresAfterSeconds = 10; // Envelope expires after 10s
    
    private readonly Action<int> _handler;
    private readonly string _name;
    private readonly HashSet<ChannelFeature> _features;
    private readonly ILoggerService _loggerService;
    private readonly Lock _mutex = new();

    // Sample rate and timing
    private int _sampleRateHz;
    private int _framesNeeded;
    private int _mixerSampleRateHz = 48000; // Default mixer rate

    // Volume gains - mirrors DOSBox volume system
    private AudioFrame _userVolumeGain = new(1.0f, 1.0f);
    private AudioFrame _appVolumeGain = new(1.0f, 1.0f);
    private float _db0VolumeGain = 1.0f;
    private AudioFrame _combinedVolumeGain = new(1.0f, 1.0f);
    
    // Resampling state - mirrors DOSBox lerp_upsampler
    private bool _doLerpUpsample;
    private double _lerpPhase;
    private AudioFrame _lerpPrevFrame;
    private AudioFrame _lerpNextFrame;
    
    // Zero-order-hold upsampler state - mirrors DOSBox zoh_upsampler
    private bool _doZohUpsample;
    private float _zohStep;
    private float _zohPos;
    private int _zohTargetRateHz;
    
    // Speex resampler state - pure C# implementation
    // Mirrors DOSBox: lazily initialized, not created in constructor
    // Replaces P/Invoke version with faithful C# port
    // Initialized ONCE when first needed (see ConfigureResampler)
    private Bufdio.Spice86.SpeexResamplerCSharp? _speexResampler;
    
    // Resampling mode flag - mirrors DOSBox do_resample
    private bool _doResample;
    
    // Resample method - mirrors DOSBox resample_method
    // DOSBox default: ResampleMethod::Resample (always use Speex)
    private ResampleMethod _resampleMethod = ResampleMethod.Resample;
    
    // Convert buffer - mirrors DOSBox convert_buffer
    // Used as intermediate buffer during sample conversion and resampling
    // Matches DOSBox: std::vector<AudioFrame> convert_buffer
    private readonly List<AudioFrame> _convertBuffer = new();

    // Channel mapping
    private StereoLine _outputMap = new() { Left = LineIndex.Left, Right = LineIndex.Right };
    private StereoLine _channelMap = new() { Left = LineIndex.Left, Right = LineIndex.Right };

    // Frame buffers - matches DOSBox audio_frames
    public List<AudioFrame> AudioFrames { get; } = new();

    private AudioFrame _prevFrame = new(0.0f, 0.0f);
    private AudioFrame _nextFrame = new(0.0f, 0.0f);

    // State flags
    public bool IsEnabled { get; private set; }
    private bool _lastSamplesWereStereo;
    
    // mirrors DOSBox mixer.h:392
    // Tracks whether last AddSamples call was silence (for debugging/optimization)
    private bool _lastSamplesWereSilence = true;
    
    /// <summary>
    /// Gets whether the last samples added were silence.
    /// Mirrors DOSBox mixer.h:392
    /// </summary>
    public bool LastSamplesWereSilence {
        get {
            lock (_mutex) {
                return _lastSamplesWereSilence;
            }
        }
    }
    
    // Peak amplitude - mirrors DOSBox mixer.h:381
    // Defines the peak sample amplitude we can expect in this channel.
    // Default to signed 16bit max
    private int _peakAmplitude = 32767; // Max16BitSampleValue
    
    // Effect sends - mirrors DOSBox per-channel effect state
    private bool _doCrossfeed;
    private float _crossfeedStrength;
    private float _crossfeedPanLeft;
    private float _crossfeedPanRight;
    
    private bool _doReverbSend;
    private float _reverbLevel;
    private float _reverbSendGain;
    
    private bool _doChorusSend;
    private float _chorusLevel;
    private float _chorusSendGain;

    // Noise gate state - mirrors DOSBox noise_gate struct
    private readonly NoiseGate _noiseGate = new();
    private float _noiseGateThresholdDb = -60.0f;
    private float _noiseGateAttackTimeMs = 1.0f;
    private float _noiseGateReleaseTimeMs = 20.0f;
    private bool _doNoiseGate;

    // Per-channel filter state - mirrors DOSBox filters struct
    private readonly HighPass[] _highPassFilters = new HighPass[2] { new HighPass(), new HighPass() };
    private FilterState _highPassFilterState = FilterState.Off;
    private int _highPassFilterOrder;
    private int _highPassFilterCutoffHz;

    private readonly LowPass[] _lowPassFilters = new LowPass[2] { new LowPass(), new LowPass() };
    private FilterState _lowPassFilterState = FilterState.Off;
    private int _lowPassFilterOrder;
    private int _lowPassFilterCutoffHz;
    
    // Sleep/wake state - mirrors DOSBox sleeper
    private readonly Sleeper _sleeper;
    private bool _doSleep;

    // Envelope - mirrors DOSBox envelope
    private readonly Envelope _envelope;

    public MixerChannel(
        Action<int> handler,
        string name,
        HashSet<ChannelFeature> features,
        ILoggerService loggerService) {
        
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _name = name ?? throw new ArgumentNullException(nameof(name));
        _features = features ?? throw new ArgumentNullException(nameof(features));
        _loggerService = loggerService ?? throw new ArgumentNullException(nameof(loggerService));
        
        // Mirrors DOSBox Staging MixerChannel constructor (mixer.cpp:305-314)
        // NO Speex resampler initialization here - it's created lazily in ConfigureResampler()
        // This ensures the resampler is only created when actually needed
        
        // Initialize sleep/wake mechanism - mirrors DOSBox mixer.cpp:313
        _doSleep = HasFeature(ChannelFeature.Sleep);
        _sleeper = new Sleeper(this);
        
        // Initialize envelope - mirrors DOSBox mixer.cpp:305
        _envelope = new Envelope(name);
    }

    /// <summary>
    /// Gets the channel name.
    /// </summary>
    public string GetName() {
        lock (_mutex) {
            return _name;
        }
    }
    
    /// <summary>
    /// Gets whether reverb send is enabled for this channel.
    /// </summary>
    public bool DoReverbSend {
        get {
            lock (_mutex) {
                return _doReverbSend;
            }
        }
    }
    
    /// <summary>
    /// Gets the reverb send gain for this channel.
    /// </summary>
    public float ReverbSendGain {
        get {
            lock (_mutex) {
                return _reverbSendGain;
            }
        }
    }
    
    /// <summary>
    /// Gets whether chorus send is enabled for this channel.
    /// </summary>
    public bool DoChorusSend {
        get {
            lock (_mutex) {
                return _doChorusSend;
            }
        }
    }
    
    /// <summary>
    /// Gets the chorus send gain for this channel.
    /// </summary>
    public float ChorusSendGain {
        get {
            lock (_mutex) {
                return _chorusSendGain;
            }
        }
    }

    /// <summary>
    /// Gets the channel sample rate.
    /// </summary>
    public int GetSampleRate() {
        lock (_mutex) {
            return _sampleRateHz;
        }
    }

    /// <summary>
    /// Sets the channel sample rate.
    /// Mirrors DOSBox SetSampleRate() with resampler configuration.
    /// </summary>
    public void SetSampleRate(int sampleRateHz) {
        lock (_mutex) {
            _sampleRateHz = sampleRateHz;
            
            // Update envelope with new sample rate - mirrors DOSBox mixer.cpp:1106-1109
            _envelope.Update(_sampleRateHz, 
                           _peakAmplitude, 
                           EnvelopeMaxExpansionOverMs, 
                           EnvelopeExpiresAfterSeconds);
            
            // Initialize noise gate if enabled - mirrors DOSBox mixer.cpp:1111-1113
            if (_doNoiseGate) {
                InitNoiseGate();
            }
            
            // Initialize filters if enabled - mirrors DOSBox mixer.cpp:1115-1120
            if (_highPassFilterState == FilterState.On) {
                InitHighPassFilter();
            }
            if (_lowPassFilterState == FilterState.On) {
                InitLowPassFilter();
            }
            
            // Configure resampler - mirrors DOSBox mixer.cpp:1122
            ConfigureResampler();
        }
    }
    
    /// <summary>
    /// Sets the mixer sample rate for resampling calculations.
    /// </summary>
    public void SetMixerSampleRate(int mixerSampleRateHz) {
        lock (_mutex) {
            _mixerSampleRateHz = mixerSampleRateHz;
        }
    }
    
    /// <summary>
    /// Gets the number of frames per tick.
    /// Mirrors DOSBox GetFramesPerTick() from mixer.cpp:1142
    /// </summary>
    public float GetFramesPerTick() {
        lock (_mutex) {
            float stretchFactor = (float)_sampleRateHz / _mixerSampleRateHz;
            // PIT_TICK_RATE is 1000.0f in DOSBox (1 ms per tick)
            const float MillisPerTick = 1.0f;
            return (_sampleRateHz / 1000.0f) * MillisPerTick * stretchFactor;
        }
    }
    
    /// <summary>
    /// Gets the number of frames per block.
    /// Mirrors DOSBox GetFramesPerBlock() from mixer.cpp:1152
    /// </summary>
    public float GetFramesPerBlock() {
        lock (_mutex) {
            float stretchFactor = (float)_sampleRateHz / _mixerSampleRateHz;
            // Assuming default blocksize of 1024 (would need to get from mixer)
            int blocksize = 1024;
            return blocksize * stretchFactor;
        }
    }
    
    /// <summary>
    /// Gets milliseconds per frame for this channel.
    /// Mirrors DOSBox GetMillisPerFrame() from mixer.cpp:1162
    /// </summary>
    public double GetMillisPerFrame() {
        lock (_mutex) {
            return 1000.0 / _sampleRateHz;
        }
    }
    
    /// <summary>
    /// Sets the peak amplitude for this channel.
    /// Mirrors DOSBox SetPeakAmplitude() from mixer.cpp:1172
    /// </summary>
    public void SetPeakAmplitude(int peak) {
        lock (_mutex) {
            _peakAmplitude = peak;
            // Update envelope with new peak amplitude - mirrors DOSBox mixer.cpp:1177-1180
            _envelope.Update(_sampleRateHz, 
                           _peakAmplitude, 
                           EnvelopeMaxExpansionOverMs, 
                           EnvelopeExpiresAfterSeconds);
        }
    }
    
    /// <summary>
    /// Initializes the linear interpolation upsampler state.
    /// Mirrors DOSBox InitLerpUpsamplerState() from mixer.cpp:1590
    /// </summary>
    private void InitLerpUpsamplerState() {
        _lerpPhase = 0.0;
        _lerpPrevFrame = new AudioFrame(0.0f, 0.0f);
        _lerpNextFrame = new AudioFrame(0.0f, 0.0f);
    }
    
    /// <summary>
    /// Initializes the zero-order-hold upsampler state.
    /// Mirrors DOSBox InitZohUpsamplerState() from mixer.cpp:1577
    /// </summary>
    private void InitZohUpsamplerState() {
        if (_sampleRateHz >= _zohTargetRateHz) {
            throw new InvalidOperationException("ZoH upsampler requires channel rate < target rate");
        }
        
        _zohStep = (float)_sampleRateHz / _zohTargetRateHz;
        _zohPos = 0.0f;
    }
    
    /// <summary>
    /// Sets the zero-order-hold upsampler target rate.
    /// Mirrors DOSBox SetZeroOrderHoldUpsamplerTargetRate() from mixer.cpp:1558
    /// </summary>
    public void SetZeroOrderHoldUpsamplerTargetRate(int targetRateHz) {
        if (targetRateHz <= 0) {
            throw new ArgumentException("Target rate must be positive", nameof(targetRateHz));
        }
        
        lock (_mutex) {
            _zohTargetRateHz = targetRateHz;
            ConfigureResampler();
        }
    }
    
    /// <summary>
    /// Sets the resample method for this channel.
    /// Mirrors DOSBox SetResampleMethod() from mixer.cpp:1604
    /// </summary>
    public void SetResampleMethod(ResampleMethod method) {
        lock (_mutex) {
            _resampleMethod = method;
            ConfigureResampler();
        }
    }
    
    /// <summary>
    /// Configures the resampler based on channel rate and resample method.
    /// Mirrors DOSBox ConfigureResampler() from mixer.cpp:935-1052
    /// </summary>
    private void ConfigureResampler() {
        int channelRateHz = _sampleRateHz;
        int mixerRateHz = _mixerSampleRateHz;
        
        // Reset all resampling flags - mirrors DOSBox mixer.cpp:946-948
        _doLerpUpsample = false;
        _doZohUpsample = false;
        _doResample = false;
        
        // Lambda to configure Speex resampler - mirrors DOSBox mixer.cpp:950-999
        void ConfigureSpeexResampler(int inRateHz) {
            uint speexInRate = (uint)inRateHz;
            uint speexOutRate = (uint)mixerRateHz;
            
            // Only init the resampler once - mirrors DOSBox mixer.cpp:955
            if (_speexResampler == null) {
                // Always stereo - mirrors DOSBox mixer.cpp:958
                // Quality 5 - mirrors DOSBox mixer.cpp:984
                _speexResampler = new Bufdio.Spice86.SpeexResamplerCSharp(
                    SpeexChannels,
                    speexInRate,
                    speexOutRate,
                    SpeexQuality);
            }
            
            // Update rates if resampler exists - mirrors DOSBox mixer.cpp:993
            _speexResampler.SetRate(speexInRate, speexOutRate);
            
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Debug)) {
                _loggerService.Debug(
                    "MIXER: {Name}: Speex resampler is on, input rate: {InRate} Hz, output rate: {OutRate} Hz",
                    _name, inRateHz, mixerRateHz);
            }
        }
        
        // Configure resampling based on method - mirrors DOSBox mixer.cpp:1001-1051
        switch (_resampleMethod) {
            case ResampleMethod.LerpUpsampleOrResample:
                if (channelRateHz < mixerRateHz) {
                    _doLerpUpsample = true;
                    InitLerpUpsamplerState();
                } else if (channelRateHz > mixerRateHz) {
                    _doResample = true;
                    ConfigureSpeexResampler(channelRateHz);
                } else {
                    // channel_rate_hz == mixer_rate_hz
                    // no resampling is needed
                }
                break;
                
            case ResampleMethod.ZeroOrderHoldAndResample:
                if (channelRateHz < _zohTargetRateHz) {
                    _doZohUpsample = true;
                    InitZohUpsamplerState();
                    if (_zohTargetRateHz != mixerRateHz) {
                        _doResample = true;
                        ConfigureSpeexResampler(_zohTargetRateHz);
                    }
                } else {
                    // channel_rate_hz >= zoh_upsampler.target_rate_hz
                    // We cannot ZoH upsample, but we might need to resample
                    if (channelRateHz != mixerRateHz) {
                        _doResample = true;
                        ConfigureSpeexResampler(channelRateHz);
                    }
                }
                break;
                
            case ResampleMethod.Resample:
                if (channelRateHz != mixerRateHz) {
                    _doResample = true;
                    ConfigureSpeexResampler(channelRateHz);
                }
                break;
        }
    }
    
    /// <summary>
    /// Checks if the channel has a specific feature.
    /// </summary>
    public bool HasFeature(ChannelFeature feature) {
        lock (_mutex) {
            return _features.Contains(feature);
        }
    }

    /// <summary>
    /// Gets all channel features.
    /// </summary>
    public HashSet<ChannelFeature> GetFeatures() {
        lock (_mutex) {
            return new HashSet<ChannelFeature>(_features);
        }
    }

    /// <summary>
    /// Sets the 0dB scalar for volume normalization.
    /// </summary>
    public void Set0dbScalar(float scalar) {
        lock (_mutex) {
            _db0VolumeGain = scalar;
            UpdateCombinedVolume();
        }
    }

    /// <summary>
    /// Gets the user volume (set by MIXER command).
    /// </summary>
    public AudioFrame GetUserVolume() {
        lock (_mutex) {
            return _userVolumeGain;
        }
    }

    /// <summary>
    /// Sets the user volume (set by MIXER command).
    /// </summary>
    public void SetUserVolume(AudioFrame gain) {
        lock (_mutex) {
            _userVolumeGain = gain;
            UpdateCombinedVolume();
        }
    }

    /// <summary>
    /// Gets the application volume (set programmatically by DOS programs).
    /// </summary>
    public AudioFrame GetAppVolume() {
        lock (_mutex) {
            return _appVolumeGain;
        }
    }

    /// <summary>
    /// Sets the application volume (set programmatically by DOS programs).
    /// </summary>
    public void SetAppVolume(AudioFrame gain) {
        lock (_mutex) {
            float clampedLeft = Math.Clamp(gain.Left, 0.0f, 1.0f);
            float clampedRight = Math.Clamp(gain.Right, 0.0f, 1.0f);
            _appVolumeGain = new AudioFrame(clampedLeft, clampedRight);
            UpdateCombinedVolume();
        }
    }

    /// <summary>
    /// Updates the combined volume gain (user * app * db0).
    /// </summary>
    private void UpdateCombinedVolume() {
        _combinedVolumeGain = new AudioFrame(
            _userVolumeGain.Left * _appVolumeGain.Left * _db0VolumeGain,
            _userVolumeGain.Right * _appVolumeGain.Right * _db0VolumeGain
        );
    }

    /// <summary>
    /// Sets the channel mapping (mono to stereo, or stereo swap).
    /// </summary>
    public void SetChannelMap(StereoLine map) {
        lock (_mutex) {
            _channelMap = map;
        }
    }

    /// <summary>
    /// Gets the channel mapping.
    /// </summary>
    public StereoLine GetChannelMap() {
        lock (_mutex) {
            return _channelMap;
        }
    }

    /// <summary>
    /// Sets the output line mapping.
    /// </summary>
    public void SetLineoutMap(StereoLine map) {
        lock (_mutex) {
            _outputMap = map;
        }
    }

    /// <summary>
    /// Gets the output line mapping.
    /// </summary>
    public StereoLine GetLineoutMap() {
        lock (_mutex) {
            return _outputMap;
        }
    }
    
    /// <summary>
    /// Describes the lineout configuration in human-readable form.
    /// Mirrors DOSBox DescribeLineout() from mixer.cpp:2319
    /// </summary>
    public string DescribeLineout() {
        lock (_mutex) {
            if (!HasFeature(ChannelFeature.Stereo)) {
                return "Mono";
            }
            if (_outputMap.Equals(StereoLine.StereoMap)) {
                return "Stereo";
            }
            if (_outputMap.Equals(StereoLine.ReverseMap)) {
                return "Reverse";
            }
            return "Unknown";
        }
    }
    
    /// <summary>
    /// Gets the current channel settings.
    /// Mirrors DOSBox GetSettings() from mixer.cpp:2339
    /// </summary>
    public MixerChannelSettings GetSettings() {
        lock (_mutex) {
            MixerChannelSettings settings = new() {
                IsEnabled = IsEnabled,
                UserVolumeGain = GetUserVolume(),
                LineoutMap = GetLineoutMap(),
                CrossfeedStrength = GetCrossfeedStrength(),
                ReverbLevel = GetReverbLevel(),
                ChorusLevel = GetChorusLevel()
            };
            return settings;
        }
    }
    
    /// <summary>
    /// Sets the channel settings from a saved configuration.
    /// Mirrors DOSBox SetSettings() from mixer.cpp:2355
    /// </summary>
    public void SetSettings(MixerChannelSettings settings) {
        lock (_mutex) {
            IsEnabled = settings.IsEnabled;
            SetUserVolume(settings.UserVolumeGain);
            SetLineoutMap(settings.LineoutMap);
            
            // Only set effect levels if effects are enabled
            // (would need access to mixer state to check do_crossfeed, do_reverb, do_chorus)
            // For now, always set them
            SetCrossfeedStrength(settings.CrossfeedStrength);
            SetReverbLevel(settings.ReverbLevel);
            SetChorusLevel(settings.ChorusLevel);
        }
    }

    /// <summary>
    /// Enables or disables the channel.
    /// </summary>
    public void Enable(bool shouldEnable) {
        if (IsEnabled == shouldEnable) {
            return;
        }

        lock (_mutex) {
            if (!shouldEnable) {
                // Clear state when disabling - mirrors DOSBox mixer.cpp:886-892
                _framesNeeded = 0;
                AudioFrames.Clear();
                _prevFrame = new AudioFrame(0.0f, 0.0f);
                _nextFrame = new AudioFrame(0.0f, 0.0f);
                
                // Clear resampler state - mirrors DOSBox mixer.cpp:892
                ClearResampler();
            }

            IsEnabled = shouldEnable;
        }
    }
    
    /// <summary>
    /// Clears and resets all resampler state.
    /// Mirrors DOSBox ClearResampler() from mixer.cpp:1055-1076
    /// </summary>
    private void ClearResampler() {
        if (_doLerpUpsample) {
            InitLerpUpsamplerState();
        }
        if (_doZohUpsample) {
            InitZohUpsamplerState();
        }
        if (_doResample && _speexResampler != null) {
            // Reset Speex resampler memory and skip zeros
            // Mirrors DOSBox mixer.cpp:1067-1068
            _speexResampler.Reset();
            _speexResampler.SkipZeros();
            
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Debug)) {
                int inputLatency = _speexResampler.GetInputLatency();
                _loggerService.Debug(
                    "MIXER: {Name}: Speex resampler cleared and primed {Latency}-frame input queue",
                    _name, inputLatency);
            }
        }
    }

    /// <summary>
    /// Requests frames from the channel handler and fills the audio buffer.
    /// Mirrors DOSBox Mix() from mixer.cpp:1183-1211 EXACTLY
    /// </summary>
    public void Mix(int framesRequested) {
        if (framesRequested <= 0) {
            throw new ArgumentException("Frames requested must be positive", nameof(framesRequested));
        }
        
        if (!IsEnabled) {
            return;
        }

        _framesNeeded = framesRequested;

        // Mirrors DOSBox mixer.cpp:1193-1210
        // Simple loop that calls handler until we have enough frames
        while (_framesNeeded > AudioFrames.Count) {
            int framesRemaining;
            lock (_mutex) {
                // Calculate stretch factor based on sample rates
                // Mirrors DOSBox mixer.cpp:1196-1197
                float stretchFactor = (float)_sampleRateHz / (float)_mixerSampleRateHz;
                
                // Calculate how many frames we still need
                // Mirrors DOSBox mixer.cpp:1199-1201
                framesRemaining = (int)Math.Ceiling(
                    (_framesNeeded - AudioFrames.Count) * stretchFactor);
                
                // Avoid underflow - mirrors DOSBox mixer.cpp:1203-1206
                if (framesRemaining <= 0) {
                    break;
                }
            }
            
            // Call handler outside the lock - mirrors DOSBox mixer.cpp:1208-1209
            _handler(framesRemaining);
        }
    }
    
    /// <summary>
    /// Sets the crossfeed strength for this channel.
    /// Mirrors DOSBox SetCrossfeedStrength() from mixer.cpp:1617
    /// </summary>
    public void SetCrossfeedStrength(float strength) {
        lock (_mutex) {
            _crossfeedStrength = Math.Clamp(strength, 0.0f, 1.0f);
            _doCrossfeed = HasFeature(ChannelFeature.Stereo) && _crossfeedStrength > 0.0f;
            
            if (!_doCrossfeed) {
                _crossfeedStrength = 0.0f;
                return;
            }
            
            // Map [0, 1] range to [0.5, 0] - mirrors DOSBox logic
            float p = (1.0f - _crossfeedStrength) / 2.0f;
            const float center = 0.5f;
            _crossfeedPanLeft = center - p;
            _crossfeedPanRight = center + p;
        }
    }
    
    /// <summary>
    /// Gets the crossfeed strength for this channel.
    /// Mirrors DOSBox GetCrossfeedStrength() from mixer.cpp:1650
    /// </summary>
    public float GetCrossfeedStrength() {
        lock (_mutex) {
            return _crossfeedStrength;
        }
    }
    
    /// <summary>
    /// Sets the reverb send level for this channel.
    /// Mirrors DOSBox SetReverbLevel() from mixer.cpp:1656
    /// </summary>
    public void SetReverbLevel(float level) {
        lock (_mutex) {
            const float levelMin = 0.0f;
            const float levelMax = 1.0f;
            const float levelMinDb = -40.0f;
            const float levelMaxDb = 0.0f;
            
            level = Math.Clamp(level, levelMin, levelMax);
            _doReverbSend = HasFeature(ChannelFeature.ReverbSend) && level > levelMin;
            
            if (!_doReverbSend) {
                _reverbLevel = levelMin;
                _reverbSendGain = 0.0f;
                return;
            }
            
            _reverbLevel = level;
            
            // Remap level to decibels and convert to gain
            float levelDb = Remap(levelMin, levelMax, levelMinDb, levelMaxDb, level);
            _reverbSendGain = DecibelToGain(levelDb);
        }
    }
    
    /// <summary>
    /// Gets the reverb send level for this channel.
    /// Mirrors DOSBox GetReverbLevel() from mixer.cpp:1694
    /// </summary>
    public float GetReverbLevel() {
        lock (_mutex) {
            return _reverbLevel;
        }
    }
    
    /// <summary>
    /// Sets the chorus send level for this channel.
    /// Mirrors DOSBox SetChorusLevel() from mixer.cpp:1700
    /// </summary>
    public void SetChorusLevel(float level) {
        lock (_mutex) {
            const float levelMin = 0.0f;
            const float levelMax = 1.0f;
            const float levelMinDb = -24.0f;
            const float levelMaxDb = 0.0f;
            
            level = Math.Clamp(level, levelMin, levelMax);
            _doChorusSend = HasFeature(ChannelFeature.ChorusSend) && level > levelMin;
            
            if (!_doChorusSend) {
                _chorusLevel = levelMin;
                _chorusSendGain = 0.0f;
                return;
            }
            
            _chorusLevel = level;
            
            // Remap level to decibels and convert to gain
            float levelDb = Remap(levelMin, levelMax, levelMinDb, levelMaxDb, level);
            _chorusSendGain = DecibelToGain(levelDb);
        }
    }
    
    /// <summary>
    /// Gets the chorus send level for this channel.
    /// Mirrors DOSBox GetChorusLevel() from mixer.cpp:1738
    /// </summary>
    public float GetChorusLevel() {
        lock (_mutex) {
            return _chorusLevel;
        }
    }

    // Noise Gate Configuration
    // =========================
    // Mirrors DOSBox mixer.h lines 209-211

    /// <summary>
    /// Configures the noise gate with operating parameters.
    /// Mirrors DOSBox ConfigureNoiseGate() from mixer.cpp (referenced in mixer.h:209-210)
    /// </summary>
    /// <param name="thresholdDb">Threshold in dB below which signal is gated</param>
    /// <param name="attackTimeMs">Attack time in milliseconds</param>
    /// <param name="releaseTimeMs">Release time in milliseconds</param>
    public void ConfigureNoiseGate(float thresholdDb, float attackTimeMs, float releaseTimeMs) {
        if (attackTimeMs <= 0.0f) {
            throw new ArgumentOutOfRangeException(nameof(attackTimeMs), "Attack time must be positive");
        }
        if (releaseTimeMs <= 0.0f) {
            throw new ArgumentOutOfRangeException(nameof(releaseTimeMs), "Release time must be positive");
        }

        lock (_mutex) {
            _noiseGateThresholdDb = thresholdDb;
            _noiseGateAttackTimeMs = attackTimeMs;
            _noiseGateReleaseTimeMs = releaseTimeMs;

            InitNoiseGate();
        }
    }

    /// <summary>
    /// Enables or disables the noise gate processor.
    /// Mirrors DOSBox EnableNoiseGate() from mixer.cpp (referenced in mixer.h:211)
    /// </summary>
    /// <param name="enabled">True to enable, false to disable</param>
    public void EnableNoiseGate(bool enabled) {
        lock (_mutex) {
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
                _loggerService.Information("{Channel}: Noise gate {State}",
                    _name, enabled ? "enabled" : "disabled");
            }
            _doNoiseGate = enabled;
        }
    }

    /// <summary>
    /// Initializes the noise gate processor with current configuration.
    /// Mirrors DOSBox InitNoiseGate() from mixer.cpp
    /// </summary>
    private void InitNoiseGate() {
        if (_noiseGateAttackTimeMs <= 0.0f || _noiseGateReleaseTimeMs <= 0.0f) {
            throw new InvalidOperationException("Noise gate attack and release times must be positive");
        }

        lock (_mutex) {
            const int Db0fsSampleValue = short.MaxValue; // Max16BitSampleValue
            _noiseGate.Configure(_sampleRateHz,
                                Db0fsSampleValue,
                                _noiseGateThresholdDb,
                                _noiseGateAttackTimeMs,
                                _noiseGateReleaseTimeMs);
        }
    }

    // Per-Channel Filter Configuration
    // =================================
    // Mirrors DOSBox mixer.h lines 213-218

    /// <summary>
    /// Gets the state of the high-pass filter.
    /// Mirrors DOSBox GetHighPassFilterState() from mixer.cpp (referenced in mixer.h:217)
    /// </summary>
    public FilterState GetHighPassFilterState() {
        lock (_mutex) {
            return _highPassFilterState;
        }
    }

    /// <summary>
    /// Sets the high-pass filter on or off.
    /// Mirrors DOSBox SetHighPassFilter() from mixer.cpp (referenced in mixer.h:213)
    /// </summary>
    public void SetHighPassFilter(FilterState state) {
        lock (_mutex) {
            _highPassFilterState = state;

            if (_highPassFilterState == FilterState.On) {
                if (_highPassFilterOrder <= 0 || _highPassFilterCutoffHz <= 0) {
                    throw new InvalidOperationException(
                        "High-pass filter must be configured before enabling");
                }

                if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
                    _loggerService.Information(
                        "{Channel}: High-pass filter enabled (order={Order}, cutoff={Cutoff}Hz)",
                        _name, _highPassFilterOrder, _highPassFilterCutoffHz);
                }
            }
        }
    }

    /// <summary>
    /// Gets the state of the low-pass filter.
    /// Mirrors DOSBox GetLowPassFilterState() from mixer.cpp (referenced in mixer.h:217)
    /// </summary>
    public FilterState GetLowPassFilterState() {
        lock (_mutex) {
            return _lowPassFilterState;
        }
    }

    /// <summary>
    /// Sets the low-pass filter on or off.
    /// Mirrors DOSBox SetLowPassFilter() from mixer.cpp (referenced in mixer.h:214)
    /// </summary>
    public void SetLowPassFilter(FilterState state) {
        lock (_mutex) {
            _lowPassFilterState = state;

            if (_lowPassFilterState == FilterState.On) {
                if (_lowPassFilterOrder <= 0 || _lowPassFilterCutoffHz <= 0) {
                    throw new InvalidOperationException(
                        "Low-pass filter must be configured before enabling");
                }

                if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
                    _loggerService.Information(
                        "{Channel}: Low-pass filter enabled (order={Order}, cutoff={Cutoff}Hz)",
                        _name, _lowPassFilterOrder, _lowPassFilterCutoffHz);
                }
            }
        }
    }

    /// <summary>
    /// Configures the high-pass filter parameters.
    /// Mirrors DOSBox ConfigureHighPassFilter() from mixer.cpp (referenced in mixer.h:217)
    /// </summary>
    /// <param name="order">Filter order (1-16)</param>
    /// <param name="cutoffFreqHz">Cutoff frequency in Hz</param>
    public void ConfigureHighPassFilter(int order, int cutoffFreqHz) {
        const int MaxFilterOrder = 16; // Mirrors DOSBox MaxFilterOrder

        if (order <= 0 || order > MaxFilterOrder) {
            throw new ArgumentOutOfRangeException(nameof(order),
                $"Filter order must be between 1 and {MaxFilterOrder}");
        }
        if (cutoffFreqHz <= 0) {
            throw new ArgumentOutOfRangeException(nameof(cutoffFreqHz),
                "Cutoff frequency must be positive");
        }

        lock (_mutex) {
            _highPassFilterOrder = order;
            _highPassFilterCutoffHz = cutoffFreqHz;

            InitHighPassFilter();
        }
    }

    /// <summary>
    /// Initializes the high-pass filter with current configuration.
    /// Mirrors DOSBox InitHighPassFilter() from mixer.cpp
    /// </summary>
    private void InitHighPassFilter() {
        const int MaxFilterOrder = 16;

        if (_highPassFilterOrder <= 0 || _highPassFilterOrder > MaxFilterOrder) {
            throw new InvalidOperationException("High-pass filter order is invalid");
        }
        if (_highPassFilterCutoffHz <= 0) {
            throw new InvalidOperationException("High-pass filter cutoff frequency is invalid");
        }

        lock (_mutex) {
            int mixerSampleRateHz = 48000; // TODO: Get from mixer
            foreach (HighPass filter in _highPassFilters) {
                filter.Setup(_highPassFilterOrder, mixerSampleRateHz, _highPassFilterCutoffHz);
            }
        }
    }

    /// <summary>
    /// Configures the low-pass filter parameters.
    /// Mirrors DOSBox ConfigureLowPassFilter() from mixer.cpp (referenced in mixer.h:218)
    /// </summary>
    /// <param name="order">Filter order (1-16)</param>
    /// <param name="cutoffFreqHz">Cutoff frequency in Hz</param>
    public void ConfigureLowPassFilter(int order, int cutoffFreqHz) {
        const int MaxFilterOrder = 16; // Mirrors DOSBox MaxFilterOrder

        if (order <= 0 || order > MaxFilterOrder) {
            throw new ArgumentOutOfRangeException(nameof(order),
                $"Filter order must be between 1 and {MaxFilterOrder}");
        }
        if (cutoffFreqHz <= 0) {
            throw new ArgumentOutOfRangeException(nameof(cutoffFreqHz),
                "Cutoff frequency must be positive");
        }

        lock (_mutex) {
            _lowPassFilterOrder = order;
            _lowPassFilterCutoffHz = cutoffFreqHz;

            InitLowPassFilter();
        }
    }

    /// <summary>
    /// Initializes the low-pass filter with current configuration.
    /// Mirrors DOSBox InitLowPassFilter() from mixer.cpp
    /// </summary>
    private void InitLowPassFilter() {
        const int MaxFilterOrder = 16;

        if (_lowPassFilterOrder <= 0 || _lowPassFilterOrder > MaxFilterOrder) {
            throw new InvalidOperationException("Low-pass filter order is invalid");
        }
        if (_lowPassFilterCutoffHz <= 0) {
            throw new InvalidOperationException("Low-pass filter cutoff frequency is invalid");
        }

        lock (_mutex) {
            int mixerSampleRateHz = 48000; // TODO: Get from mixer
            foreach (LowPass filter in _lowPassFilters) {
                filter.Setup(_lowPassFilterOrder, mixerSampleRateHz, _lowPassFilterCutoffHz);
            }
        }
    }
    
    /// <summary>
    /// Remaps a value from one range to another.
    /// Helper for effect level calculations.
    /// </summary>
    private static float Remap(float inMin, float inMax, float outMin, float outMax, float value) {
        float normalized = (value - inMin) / (inMax - inMin);
        return outMin + normalized * (outMax - outMin);
    }
    
    /// <summary>
    /// Converts decibel value to linear gain.
    /// Helper for effect level calculations.
    /// </summary>
    private static float DecibelToGain(float db) {
        return (float)Math.Pow(10.0, db / 20.0);
    }

    /// <summary>
    /// Adds silence frames to fill the buffer.
    /// </summary>
    public void AddSilence() {
        lock (_mutex) {
            while (AudioFrames.Count < _framesNeeded) {
                AudioFrame frameWithGain;
                
                if (_prevFrame.Left == 0.0f && _prevFrame.Right == 0.0f) {
                    frameWithGain = new AudioFrame(0.0f, 0.0f);
                } else {
                    // Fade to silence to avoid clicks
                    const float fadeAmount = 4.0f;
                    
                    float nextLeft;
                    if (_prevFrame.Left > fadeAmount) {
                        nextLeft = _prevFrame.Left - fadeAmount;
                    } else if (_prevFrame.Left < -fadeAmount) {
                        nextLeft = _prevFrame.Left + fadeAmount;
                    } else {
                        nextLeft = 0.0f;
                    }
                    
                    float nextRight;
                    if (_prevFrame.Right > fadeAmount) {
                        nextRight = _prevFrame.Right - fadeAmount;
                    } else if (_prevFrame.Right < -fadeAmount) {
                        nextRight = _prevFrame.Right + fadeAmount;
                    } else {
                        nextRight = 0.0f;
                    }
                    
                    _nextFrame = new AudioFrame(nextLeft, nextRight);
                    
                    AudioFrame baseFrame = _lastSamplesWereStereo
                        ? _prevFrame
                        : new AudioFrame(_prevFrame.Left);
                    frameWithGain = baseFrame.Multiply(_combinedVolumeGain);
                    
                    _prevFrame = _nextFrame;
                }

                AudioFrame outFrame = new();
                outFrame[(int)_outputMap.Left] = frameWithGain.Left;
                outFrame[(int)_outputMap.Right] = frameWithGain.Right;
                
                AudioFrames.Add(outFrame);
            }
            
            // mirrors DOSBox mixer.cpp:1264
            _lastSamplesWereSilence = true;
        }
    }

    /// <summary>
    /// Converts mono 8-bit unsigned samples to AudioFrames with optional ZoH upsampling.
    /// Fills convert_buffer with converted and potentially ZoH-upsampled frames.
    /// Mirrors DOSBox ConvertSamplesAndMaybeZohUpsample() from mixer.cpp:1869
    /// </summary>
    private void ConvertSamplesAndMaybeZohUpsample_m8(ReadOnlySpan<byte> data, int numFrames) {
        _convertBuffer.Clear();
        
        LineIndex mappedOutputLeft = _outputMap.Left;
        LineIndex mappedOutputRight = _outputMap.Right;
        LineIndex mappedChannelLeft = _channelMap.Left;
        
        int pos = 0;
        while (pos < numFrames) {
            _prevFrame = _nextFrame;
            
            // Convert 8-bit unsigned to float
            _nextFrame = new AudioFrame(LookupTables.U8To16[data[pos]], LookupTables.U8To16[data[pos]]);
            
            // Apply channel mapping (mono uses left channel)
            AudioFrame frameWithGain = new AudioFrame(_prevFrame[(int)mappedChannelLeft]);
            frameWithGain = frameWithGain.Multiply(_combinedVolumeGain);
            
            // Process through envelope to prevent clicks
            _envelope.Process(false, ref frameWithGain);
            
            // Apply output mapping
            AudioFrame outFrame = new();
            outFrame[(int)mappedOutputLeft] = frameWithGain.Left;
            outFrame[(int)mappedOutputRight] = frameWithGain.Right;
            
            _convertBuffer.Add(outFrame);
            
            if (_doZohUpsample) {
                _zohPos += _zohStep;
                if (_zohPos > 1.0f) {
                    _zohPos -= 1.0f;
                    pos++;
                }
            } else {
                pos++;
            }
        }
    }
    
    /// <summary>
    /// Converts mono 16-bit signed samples to AudioFrames with optional ZoH upsampling.
    /// Fills convert_buffer with converted and potentially ZoH-upsampled frames.
    /// Mirrors DOSBox ConvertSamplesAndMaybeZohUpsample() from mixer.cpp:1869
    /// </summary>
    private void ConvertSamplesAndMaybeZohUpsample_m16(ReadOnlySpan<short> data, int numFrames) {
        _convertBuffer.Clear();
        
        LineIndex mappedOutputLeft = _outputMap.Left;
        LineIndex mappedOutputRight = _outputMap.Right;
        LineIndex mappedChannelLeft = _channelMap.Left;
        
        int pos = 0;
        while (pos < numFrames) {
            _prevFrame = _nextFrame;
            
            // Convert 16-bit signed to float
            _nextFrame = new AudioFrame(data[pos], data[pos]);
            
            // Apply channel mapping (mono uses left channel)
            AudioFrame frameWithGain = new AudioFrame(_prevFrame[(int)mappedChannelLeft]);
            frameWithGain = frameWithGain.Multiply(_combinedVolumeGain);
            
            // Process through envelope to prevent clicks
            _envelope.Process(false, ref frameWithGain);
            
            // Apply output mapping
            AudioFrame outFrame = new();
            outFrame[(int)mappedOutputLeft] = frameWithGain.Left;
            outFrame[(int)mappedOutputRight] = frameWithGain.Right;
            
            _convertBuffer.Add(outFrame);
            
            if (_doZohUpsample) {
                _zohPos += _zohStep;
                if (_zohPos > 1.0f) {
                    _zohPos -= 1.0f;
                    pos++;
                }
            } else {
                pos++;
            }
        }
    }
    
    /// <summary>
    /// Converts stereo 16-bit signed samples to AudioFrames with optional ZoH upsampling.
    /// Fills convert_buffer with converted and potentially ZoH-upsampled frames.
    /// Mirrors DOSBox ConvertSamplesAndMaybeZohUpsample() from mixer.cpp:1869
    /// </summary>
    private void ConvertSamplesAndMaybeZohUpsample_s16(ReadOnlySpan<short> data, int numFrames) {
        _convertBuffer.Clear();
        
        LineIndex mappedOutputLeft = _outputMap.Left;
        LineIndex mappedOutputRight = _outputMap.Right;
        LineIndex mappedChannelLeft = _channelMap.Left;
        LineIndex mappedChannelRight = _channelMap.Right;
        
        int pos = 0;
        while (pos < numFrames) {
            _prevFrame = _nextFrame;
            
            // Convert stereo 16-bit signed to float
            _nextFrame = new AudioFrame(data[pos * 2], data[pos * 2 + 1]);
            
            // Apply channel mapping (stereo)
            AudioFrame frameWithGain = new AudioFrame(
                _prevFrame[(int)mappedChannelLeft],
                _prevFrame[(int)mappedChannelRight]
            );
            frameWithGain = frameWithGain.Multiply(_combinedVolumeGain);
            
            // Process through envelope to prevent clicks
            _envelope.Process(true, ref frameWithGain);
            
            // Apply output mapping
            AudioFrame outFrame = new();
            outFrame[(int)mappedOutputLeft] = frameWithGain.Left;
            outFrame[(int)mappedOutputRight] = frameWithGain.Right;
            
            _convertBuffer.Add(outFrame);
            
            if (_doZohUpsample) {
                _zohPos += _zohStep;
                if (_zohPos > 1.0f) {
                    _zohPos -= 1.0f;
                    pos++;
                }
            } else {
                pos++;
            }
        }
    }
    
    /// <summary>
    /// Converts mono 32-bit float samples to AudioFrames with optional ZoH upsampling.
    /// Fills convert_buffer with converted and potentially ZoH-upsampled frames.
    /// Mirrors DOSBox ConvertSamplesAndMaybeZohUpsample() from mixer.cpp:1869
    /// </summary>
    private void ConvertSamplesAndMaybeZohUpsample_mfloat(ReadOnlySpan<float> data, int numFrames) {
        _convertBuffer.Clear();
        
        LineIndex mappedOutputLeft = _outputMap.Left;
        LineIndex mappedOutputRight = _outputMap.Right;
        LineIndex mappedChannelLeft = _channelMap.Left;
        
        int pos = 0;
        while (pos < numFrames) {
            _prevFrame = _nextFrame;
            
            // Float samples are already in the correct format
            float sample = data[pos] * 32768.0f; // Convert normalized to 16-bit range
            _nextFrame = new AudioFrame(sample, sample);
            
            // Apply channel mapping (mono uses left channel)
            AudioFrame frameWithGain = new AudioFrame(_prevFrame[(int)mappedChannelLeft]);
            frameWithGain = frameWithGain.Multiply(_combinedVolumeGain);
            
            // Process through envelope to prevent clicks
            _envelope.Process(false, ref frameWithGain);
            
            // Apply output mapping
            AudioFrame outFrame = new();
            outFrame[(int)mappedOutputLeft] = frameWithGain.Left;
            outFrame[(int)mappedOutputRight] = frameWithGain.Right;
            
            _convertBuffer.Add(outFrame);
            
            if (_doZohUpsample) {
                _zohPos += _zohStep;
                if (_zohPos > 1.0f) {
                    _zohPos -= 1.0f;
                    pos++;
                }
            } else {
                pos++;
            }
        }
    }
    
    /// <summary>
    /// Converts stereo 32-bit float samples to AudioFrames with optional ZoH upsampling.
    /// Fills convert_buffer with converted and potentially ZoH-upsampled frames.
    /// Mirrors DOSBox ConvertSamplesAndMaybeZohUpsample() from mixer.cpp:1869
    /// </summary>
    private void ConvertSamplesAndMaybeZohUpsample_sfloat(ReadOnlySpan<float> data, int numFrames) {
        _convertBuffer.Clear();
        
        LineIndex mappedOutputLeft = _outputMap.Left;
        LineIndex mappedOutputRight = _outputMap.Right;
        LineIndex mappedChannelLeft = _channelMap.Left;
        LineIndex mappedChannelRight = _channelMap.Right;
        
        int pos = 0;
        while (pos < numFrames) {
            _prevFrame = _nextFrame;
            
            // Float samples are already in int16-ranged format (like DOSBox staging)
            // DOSBox: just does static_cast<float> with no conversion
            // Mirrors DOSBox mixer.cpp:1885-1892
            float left = data[pos * 2];
            float right = data[pos * 2 + 1];
            _nextFrame = new AudioFrame(left, right);
            
            // Apply channel mapping (stereo)
            AudioFrame frameWithGain = new AudioFrame(
                _prevFrame[(int)mappedChannelLeft],
                _prevFrame[(int)mappedChannelRight]
            );
            frameWithGain = frameWithGain.Multiply(_combinedVolumeGain);
            
            // Process through envelope to prevent clicks
            _envelope.Process(true, ref frameWithGain);
            
            // Apply output mapping
            AudioFrame outFrame = new();
            outFrame[(int)mappedOutputLeft] = frameWithGain.Left;
            outFrame[(int)mappedOutputRight] = frameWithGain.Right;
            
            _convertBuffer.Add(outFrame);
            
            if (_doZohUpsample) {
                _zohPos += _zohStep;
                if (_zohPos > 1.0f) {
                    _zohPos -= 1.0f;
                    pos++;
                }
            } else {
                pos++;
            }
        }
    }
    
    /// <summary>
    /// Adds mono 8-bit unsigned samples with resampling.
    /// Mirrors DOSBox AddSamples() template from mixer.cpp:2125
    /// </summary>
    public void AddSamples_m8(int numFrames, ReadOnlySpan<byte> data) {
        if (numFrames <= 0) {
            return;
        }

        lock (_mutex) {
            _lastSamplesWereStereo = false;

            // All possible resampling scenarios (mirrors DOSBox mixer.cpp:2136-2149):
            // - No upsampling or resampling
            // - LERP  upsampling only
            // - ZoH   upsampling only
            // - Speex resampling only
            // - ZoH upsampling followed by Speex resampling

            // Zero-order-hold upsampling is performed in ConvertSamplesAndMaybeZohUpsample
            // to reduce the number of temporary buffers and simplify the code.

            // Assert that we're not attempting to do both LERP and Speex resample
            // We can do one or neither
            if (_doLerpUpsample && _doResample) {
                throw new InvalidOperationException("Cannot do both LERP upsample and Speex resample");
            }

            // Step 1: Convert samples and maybe apply ZoH upsampling → fills convert_buffer
            ConvertSamplesAndMaybeZohUpsample_m8(data, numFrames);

            // Starting index this function will start writing to
            // The audio_frames vector can contain previously converted/resampled audio
            int audioFramesStartingSize = AudioFrames.Count;

            // Step 2: Apply resampling if needed
            if (_doLerpUpsample) {
                // LERP upsampling - mirrors DOSBox mixer.cpp:2163-2202
                for (int i = 0; i < _convertBuffer.Count;) {
                    AudioFrame currFrame = _convertBuffer[i];

                    float t = (float)_lerpPhase;
                    AudioFrame lerpedFrame = new AudioFrame(
                        Lerp(_lerpPrevFrame.Left, currFrame.Left, t),
                        Lerp(_lerpPrevFrame.Right, currFrame.Right, t)
                    );

                    AudioFrames.Add(lerpedFrame);

                    _lerpPhase += (double)_sampleRateHz / _mixerSampleRateHz;

                    if (_lerpPhase > 1.0) {
                        _lerpPhase -= 1.0;
                        _lerpPrevFrame = currFrame;
                        i++; // Move to next input frame
                    }
                }
            } else if (_doResample) {
                // Speex resampling - mirrors DOSBox mixer.cpp:2203-2243
                ApplySpeexResampling(audioFramesStartingSize);
            } else {
                // No resampling - just copy convert_buffer to audio_frames
                // Mirrors DOSBox mixer.cpp:2244-2246
                AudioFrames.AddRange(_convertBuffer);
            }

            // Step 3: Apply in-place processing to newly added frames
            // Mirrors DOSBox mixer.cpp:2248-2268
            ApplyInPlaceProcessing(audioFramesStartingSize);
        }
    }

    /// <summary>
    /// Linear interpolation helper function.
    /// Mirrors DOSBox lerp() function.
    /// </summary>
    private static float Lerp(float a, float b, float t) {
        return a * (1.0f - t) + b * t;
    }

    /// <summary>
    /// Applies Speex resampling to convert_buffer → audio_frames.
    /// Mirrors DOSBox Speex resampling logic from mixer.cpp:2203-2243
    /// </summary>
    private void ApplySpeexResampling(int audioFramesStartingSize) {
        if (_speexResampler == null || !_speexResampler.IsInitialized) {
            // Fallback: just copy convert_buffer if resampler isn't ready
            AudioFrames.AddRange(_convertBuffer);
            return;
        }

        int inFrames = _convertBuffer.Count;
        if (inFrames == 0) {
            return;
        }

        // Estimate maximum output frames needed
        // Mirrors DOSBox estimate_max_out_frames() from mixer.cpp:1937-1948
        _speexResampler.GetRatio(out uint ratioNum, out uint ratioDen);
        int estimatedOutFrames = (int)Math.Ceiling((double)inFrames * ratioDen / ratioNum);

        // Resize audio_frames to accommodate new frames
        int targetSize = audioFramesStartingSize + estimatedOutFrames;
        while (AudioFrames.Count < targetSize) {
            AudioFrames.Add(new AudioFrame(0.0f, 0.0f));
        }

        // Prepare input buffer - convert AudioFrame[] to interleaved float[]
        float[] inputBuffer = new float[inFrames * 2];
        for (int i = 0; i < inFrames; i++) {
            inputBuffer[i * 2] = _convertBuffer[i].Left;
            inputBuffer[i * 2 + 1] = _convertBuffer[i].Right;
        }

        // Prepare output buffer
        float[] outputBuffer = new float[estimatedOutFrames * 2];

        // Process through Speex resampler (interleaved stereo)
        _speexResampler.ProcessInterleavedFloat(
            inputBuffer.AsSpan(),
            outputBuffer.AsSpan(),
            out uint inFramesConsumed,
            out uint outFramesGenerated);

        // Copy resampled frames back to audio_frames
        for (int i = 0; i < (int)outFramesGenerated; i++) {
            AudioFrames[audioFramesStartingSize + i] = new AudioFrame(
                outputBuffer[i * 2],
                outputBuffer[i * 2 + 1]);
        }

        // Trim audio_frames to actual size
        int actualSize = audioFramesStartingSize + (int)outFramesGenerated;
        if (AudioFrames.Count > actualSize) {
            AudioFrames.RemoveRange(actualSize, AudioFrames.Count - actualSize);
        }
    }

    /// <summary>
    /// Applies in-place processing (filters, crossfeed) to newly added frames.
    /// Mirrors DOSBox mixer.cpp:2248-2268
    /// </summary>
    private void ApplyInPlaceProcessing(int startIndex) {
        // Optionally gate, filter, and apply crossfeed.
        // Runs in-place over newly added frames.
        for (int i = startIndex; i < AudioFrames.Count; i++) {
            // Note: Noise gate not implemented yet
            // Note: Per-channel high-pass/low-pass filters not implemented yet

            // Apply crossfeed if enabled
            if (_doCrossfeed) {
                AudioFrames[i] = ApplyCrossfeed(AudioFrames[i]);
            }
        }
    }

    /// <summary>
    /// Applies crossfeed to a single frame.
    /// Mirrors DOSBox ApplyCrossfeed() from mixer.cpp:1951-1960
    /// </summary>
    private AudioFrame ApplyCrossfeed(AudioFrame frame) {
        // Pan mono sample using -6dB linear pan law in the stereo field
        // pan: 0.0 = left, 0.5 = center, 1.0 = right
        AudioFrame PanSample(float sample, float pan) {
            return new AudioFrame((1.0f - pan) * sample, pan * sample);
        }

        AudioFrame a = PanSample(frame.Left, _crossfeedPanLeft);
        AudioFrame b = PanSample(frame.Right, _crossfeedPanRight);
        return new AudioFrame(a.Left + b.Left, a.Right + b.Right);
    }

    /// <summary>
    /// Adds mono 16-bit signed samples with resampling.
    /// Mirrors DOSBox AddSamples() template from mixer.cpp:2125
    /// </summary>
    public void AddSamples_m16(int numFrames, ReadOnlySpan<short> data) {
        if (numFrames <= 0) {
            return;
        }

        lock (_mutex) {
            _lastSamplesWereStereo = false;

            // Assert that we're not attempting to do both LERP and Speex resample
            if (_doLerpUpsample && _doResample) {
                throw new InvalidOperationException("Cannot do both LERP upsample and Speex resample");
            }

            // Step 1: Convert samples and maybe apply ZoH upsampling → fills convert_buffer
            ConvertSamplesAndMaybeZohUpsample_m16(data, numFrames);

            // Starting index this function will start writing to
            int audioFramesStartingSize = AudioFrames.Count;

            // Step 2: Apply resampling if needed
            if (_doLerpUpsample) {
                // LERP upsampling
                for (int i = 0; i < _convertBuffer.Count;) {
                    AudioFrame currFrame = _convertBuffer[i];

                    float t = (float)_lerpPhase;
                    AudioFrame lerpedFrame = new AudioFrame(
                        Lerp(_lerpPrevFrame.Left, currFrame.Left, t),
                        Lerp(_lerpPrevFrame.Right, currFrame.Right, t)
                    );

                    AudioFrames.Add(lerpedFrame);

                    _lerpPhase += (double)_sampleRateHz / _mixerSampleRateHz;

                    if (_lerpPhase > 1.0) {
                        _lerpPhase -= 1.0;
                        _lerpPrevFrame = currFrame;
                        i++;
                    }
                }
            } else if (_doResample) {
                // Speex resampling
                ApplySpeexResampling(audioFramesStartingSize);
            } else {
                // No resampling
                AudioFrames.AddRange(_convertBuffer);
            }

            // Step 3: Apply in-place processing to newly added frames
            ApplyInPlaceProcessing(audioFramesStartingSize);
        }
    }

    /// <summary>
    /// Adds stereo 16-bit signed samples with resampling.
    /// Mirrors DOSBox AddSamples() template from mixer.cpp:2125
    /// </summary>
    public void AddSamples_s16(int numFrames, ReadOnlySpan<short> data) {
        if (numFrames <= 0) {
            return;
        }

        lock (_mutex) {
            _lastSamplesWereStereo = true;

            // Assert that we're not attempting to do both LERP and Speex resample
            if (_doLerpUpsample && _doResample) {
                throw new InvalidOperationException("Cannot do both LERP upsample and Speex resample");
            }

            // Step 1: Convert samples and maybe apply ZoH upsampling → fills convert_buffer
            ConvertSamplesAndMaybeZohUpsample_s16(data, numFrames);

            // Starting index this function will start writing to
            int audioFramesStartingSize = AudioFrames.Count;

            // Step 2: Apply resampling if needed
            if (_doLerpUpsample) {
                // LERP upsampling
                for (int i = 0; i < _convertBuffer.Count;) {
                    AudioFrame currFrame = _convertBuffer[i];

                    float t = (float)_lerpPhase;
                    AudioFrame lerpedFrame = new AudioFrame(
                        Lerp(_lerpPrevFrame.Left, currFrame.Left, t),
                        Lerp(_lerpPrevFrame.Right, currFrame.Right, t)
                    );

                    AudioFrames.Add(lerpedFrame);

                    _lerpPhase += (double)_sampleRateHz / _mixerSampleRateHz;

                    if (_lerpPhase > 1.0) {
                        _lerpPhase -= 1.0;
                        _lerpPrevFrame = currFrame;
                        i++;
                    }
                }
            } else if (_doResample) {
                // Speex resampling
                ApplySpeexResampling(audioFramesStartingSize);
            } else {
                // No resampling
                AudioFrames.AddRange(_convertBuffer);
            }

            // Step 3: Apply in-place processing to newly added frames
            ApplyInPlaceProcessing(audioFramesStartingSize);
        }
    }

    /// <summary>
    /// Adds mono 32-bit float samples with resampling.
    /// Mirrors DOSBox AddSamples_mfloat() from mixer.cpp:2285
    /// </summary>
    public void AddSamples_mfloat(int numFrames, ReadOnlySpan<float> data) {
        if (numFrames <= 0) {
            return;
        }

        lock (_mutex) {
            _lastSamplesWereStereo = false;

            // Assert that we're not attempting to do both LERP and Speex resample
            if (_doLerpUpsample && _doResample) {
                throw new InvalidOperationException("Cannot do both LERP upsample and Speex resample");
            }

            // Step 1: Convert samples and maybe apply ZoH upsampling → fills convert_buffer
            ConvertSamplesAndMaybeZohUpsample_mfloat(data, numFrames);

            // Starting index this function will start writing to
            int audioFramesStartingSize = AudioFrames.Count;

            // Step 2: Apply resampling if needed
            if (_doLerpUpsample) {
                // LERP upsampling
                for (int i = 0; i < _convertBuffer.Count;) {
                    AudioFrame currFrame = _convertBuffer[i];

                    float t = (float)_lerpPhase;
                    AudioFrame lerpedFrame = new AudioFrame(
                        Lerp(_lerpPrevFrame.Left, currFrame.Left, t),
                        Lerp(_lerpPrevFrame.Right, currFrame.Right, t)
                    );

                    AudioFrames.Add(lerpedFrame);

                    _lerpPhase += (double)_sampleRateHz / _mixerSampleRateHz;

                    if (_lerpPhase > 1.0) {
                        _lerpPhase -= 1.0;
                        _lerpPrevFrame = currFrame;
                        i++;
                    }
                }
            } else if (_doResample) {
                // Speex resampling
                ApplySpeexResampling(audioFramesStartingSize);
            } else {
                // No resampling
                AudioFrames.AddRange(_convertBuffer);
            }

            // Step 3: Apply in-place processing to newly added frames
            ApplyInPlaceProcessing(audioFramesStartingSize);
        }
    }
    
    /// <summary>
    /// Adds stereo 32-bit float samples with resampling.
    /// Mirrors DOSBox AddSamples_sfloat() from mixer.cpp:2290
    /// </summary>
    public void AddSamples_sfloat(int numFrames, ReadOnlySpan<float> data) {
        if (numFrames <= 0) {
            return;
        }

        lock (_mutex) {
            _lastSamplesWereStereo = true;

            // Assert that we're not attempting to do both LERP and Speex resample
            if (_doLerpUpsample && _doResample) {
                throw new InvalidOperationException("Cannot do both LERP upsample and Speex resample");
            }

            // Step 1: Convert samples and maybe apply ZoH upsampling → fills convert_buffer
            ConvertSamplesAndMaybeZohUpsample_sfloat(data, numFrames);

            // Starting index this function will start writing to
            int audioFramesStartingSize = AudioFrames.Count;

            // Step 2: Apply resampling if needed
            if (_doLerpUpsample) {
                // LERP upsampling
                for (int i = 0; i < _convertBuffer.Count;) {
                    AudioFrame currFrame = _convertBuffer[i];

                    float t = (float)_lerpPhase;
                    AudioFrame lerpedFrame = new AudioFrame(
                        Lerp(_lerpPrevFrame.Left, currFrame.Left, t),
                        Lerp(_lerpPrevFrame.Right, currFrame.Right, t)
                    );

                    AudioFrames.Add(lerpedFrame);

                    _lerpPhase += (double)_sampleRateHz / _mixerSampleRateHz;

                    if (_lerpPhase > 1.0) {
                        _lerpPhase -= 1.0;
                        _lerpPrevFrame = currFrame;
                        i++;
                    }
                }
            } else if (_doResample) {
                // Speex resampling
                ApplySpeexResampling(audioFramesStartingSize);
            } else {
                // No resampling
                AudioFrames.AddRange(_convertBuffer);
            }

            // Step 3: Apply in-place processing to newly added frames
            ApplyInPlaceProcessing(audioFramesStartingSize);
        }
    }

    /// <summary>
    /// Adds audio frames directly with resampling.
    /// Used for direct AudioFrame input from sources.
    /// </summary>
    public void AddAudioFrames(ReadOnlySpan<AudioFrame> frames) {
        if (frames.Length <= 0) {
            return;
        }

        lock (_mutex) {
            _lastSamplesWereStereo = true;

            // Assert that we're not attempting to do both LERP and Speex resample
            if (_doLerpUpsample && _doResample) {
                throw new InvalidOperationException("Cannot do both LERP upsample and Speex resample");
            }

            // Convert AudioFrame[] to convert_buffer with ZoH if needed
            _convertBuffer.Clear();
            
            LineIndex mappedOutputLeft = _outputMap.Left;
            LineIndex mappedOutputRight = _outputMap.Right;
            LineIndex mappedChannelLeft = _channelMap.Left;
            LineIndex mappedChannelRight = _channelMap.Right;
            
            int pos = 0;
            while (pos < frames.Length) {
                _prevFrame = _nextFrame;
                _nextFrame = frames[pos];
                
                // Apply channel mapping
                AudioFrame frameWithGain = new AudioFrame(
                    _prevFrame[(int)mappedChannelLeft],
                    _prevFrame[(int)mappedChannelRight]
                );
                frameWithGain = frameWithGain.Multiply(_combinedVolumeGain);
                
                // Process through envelope
                _envelope.Process(true, ref frameWithGain);
                
                // Apply output mapping
                AudioFrame outFrame = new();
                outFrame[(int)mappedOutputLeft] = frameWithGain.Left;
                outFrame[(int)mappedOutputRight] = frameWithGain.Right;
                
                _convertBuffer.Add(outFrame);
                
                if (_doZohUpsample) {
                    _zohPos += _zohStep;
                    if (_zohPos > 1.0f) {
                        _zohPos -= 1.0f;
                        pos++;
                    }
                } else {
                    pos++;
                }
            }

            // Starting index this function will start writing to
            int audioFramesStartingSize = AudioFrames.Count;

            // Apply resampling if needed
            if (_doLerpUpsample) {
                // LERP upsampling
                for (int i = 0; i < _convertBuffer.Count;) {
                    AudioFrame currFrame = _convertBuffer[i];

                    float t = (float)_lerpPhase;
                    AudioFrame lerpedFrame = new AudioFrame(
                        Lerp(_lerpPrevFrame.Left, currFrame.Left, t),
                        Lerp(_lerpPrevFrame.Right, currFrame.Right, t)
                    );

                    AudioFrames.Add(lerpedFrame);

                    _lerpPhase += (double)_sampleRateHz / _mixerSampleRateHz;

                    if (_lerpPhase > 1.0) {
                        _lerpPhase -= 1.0;
                        _lerpPrevFrame = currFrame;
                        i++;
                    }
                }
            } else if (_doResample) {
                // Speex resampling
                ApplySpeexResampling(audioFramesStartingSize);
            } else {
                // No resampling
                AudioFrames.AddRange(_convertBuffer);
            }

            // Apply in-place processing to newly added frames
            ApplyInPlaceProcessing(audioFramesStartingSize);
        }
    }
    
    /// <summary>
    /// Applies fade-out or signal detection to a frame if sleep is enabled.
    /// Called by mixer during frame processing.
    /// Mirrors DOSBox mixer.cpp:2420
    /// </summary>
    internal AudioFrame MaybeFadeOrListen(AudioFrame frame) {
        if (!_doSleep) {
            return frame;
        }
        
        // No lock needed here - called within mixer's processing loop
        return _sleeper.MaybeFadeOrListen(frame);
    }
    
    /// <summary>
    /// Attempts to put the channel to sleep if conditions are met.
    /// Called by mixer after frame processing.
    /// Mirrors DOSBox mixer.cpp:2441
    /// </summary>
    internal void MaybeSleep() {
        if (!_doSleep) {
            return;
        }
        
        // No lock needed here - called within mixer's processing loop
        _sleeper.MaybeSleep();
    }
    
    /// <summary>
    /// Wakes up a sleeping channel.
    /// Audio devices that use the sleep feature need to wake up the channel whenever
    /// they might prepare new samples for it (typically on IO port writes).
    /// Mirrors DOSBox mixer.cpp:2120-2125
    /// </summary>
    /// <returns>True if the channel was actually sleeping, false if already awake</returns>
    public bool WakeUp() {
        if (!_doSleep) {
            return false;
        }
        
        lock (_mutex) {
            return _sleeper.WakeUp();
        }
    }
    
    /// <summary>
    /// Configures fade-out behavior for this channel.
    /// Mirrors DOSBox mixer.cpp:1961-1967
    /// </summary>
    /// <param name="prefs">Configuration string (e.g., "true", "false", "500 500" for wait_ms and fade_ms)</param>
    /// <returns>True if configuration succeeded, false otherwise</returns>
    public bool ConfigureFadeOut(string prefs) {
        lock (_mutex) {
            return _sleeper.ConfigureFadeOut(prefs);
        }
    }
    
    /// <summary>
    /// Nested class that manages channel sleep/wake behavior with optional fade-out.
    /// Mirrors DOSBox Staging's MixerChannel::Sleeper class.
    /// Reference: DOSBox mixer.cpp lines 1960-2130
    /// </summary>
    private sealed class Sleeper {
        private readonly MixerChannel _channel;
        
        // The wait before fading or sleeping is bound between these values
        private const int MinWaitMs = 100;
        private const int DefaultWaitMs = 500;
        private const int MaxWaitMs = 5000;
        
        private AudioFrame _lastFrame;
        private long _wokenAtMs;
        private float _fadeoutLevel;
        private float _fadeoutDecrementPerMs;
        private int _fadeoutOrSleepAfterMs;
        
        private bool _wantsFadeout;
        private bool _hadSignal;
        
        public Sleeper(MixerChannel channel, int sleepAfterMs = DefaultWaitMs) {
            _channel = channel ?? throw new ArgumentNullException(nameof(channel));
            _fadeoutOrSleepAfterMs = sleepAfterMs;
            
            // The constructed sleep period is programmatically controlled
            if (sleepAfterMs < MinWaitMs || sleepAfterMs > MaxWaitMs) {
                throw new ArgumentOutOfRangeException(nameof(sleepAfterMs), 
                    $"Sleep period must be between {MinWaitMs} and {MaxWaitMs} ms");
            }
        }
        
        /// <summary>
        /// Configures fade-out behavior.
        /// Mirrors DOSBox mixer.cpp:1968-2042
        /// </summary>
        public bool ConfigureFadeOut(string prefs) {
            void SetWaitAndFade(int waitMs, int fadeMs) {
                _fadeoutOrSleepAfterMs = waitMs;
                _fadeoutDecrementPerMs = 1.0f / (float)fadeMs;
                
                // LOG_MSG equivalent - using Verbose level
                if (_channel._loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
                    _channel._loggerService.Verbose(
                        "{Channel}: Fade-out enabled (wait {Wait} ms then fade for {Fade} ms)",
                        _channel._name, waitMs, fadeMs);
                }
            }
            
            // Disable fade-out (default)
            if (HasFalse(prefs)) {
                _wantsFadeout = false;
                return true;
            }
            
            // Enable fade-out with defaults
            if (HasTrue(prefs)) {
                SetWaitAndFade(DefaultWaitMs, DefaultWaitMs);
                _wantsFadeout = true;
                return true;
            }
            
            // Let the fade-out last between 10 ms and 3 seconds
            const int MinFadeMs = 10;
            const int MaxFadeMs = 3000;
            
            // Custom setting in 'WAIT FADE' syntax, where both are milliseconds
            string[] parts = prefs.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2) {
                if (int.TryParse(parts[0], out int waitMs) && int.TryParse(parts[1], out int fadeMs)) {
                    bool waitIsValid = waitMs >= MinWaitMs && waitMs <= MaxWaitMs;
                    bool fadeIsValid = fadeMs >= MinFadeMs && fadeMs <= MaxFadeMs;
                    
                    if (waitIsValid && fadeIsValid) {
                        SetWaitAndFade(waitMs, fadeMs);
                        _wantsFadeout = true;
                        return true;
                    }
                }
            }
            
            // Otherwise inform the user and disable the fade
            if (_channel._loggerService.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
                _channel._loggerService.Warning(
                    "{Channel}: Invalid custom fadeout '{Prefs}'. " +
                    "Expected 'true', 'false', or 'WAIT FADE' where WAIT is {MinWait}-{MaxWait} ms " +
                    "and FADE is {MinFade}-{MaxFade} ms",
                    _channel._name, prefs, MinWaitMs, MaxWaitMs, MinFadeMs, MaxFadeMs);
            }
            
            _wantsFadeout = false;
            return false;
        }
        
        /// <summary>
        /// Decrements the fade level based on elapsed time.
        /// Mirrors DOSBox mixer.cpp:2029-2041
        /// </summary>
        private void DecrementFadeLevel(int awakeForMs) {
            if (awakeForMs < _fadeoutOrSleepAfterMs) {
                throw new ArgumentException("awakeForMs must be >= fadeoutOrSleepAfterMs");
            }
            
            float elapsedFadeMs = (float)(awakeForMs - _fadeoutOrSleepAfterMs);
            float decrement = _fadeoutDecrementPerMs * elapsedFadeMs;
            
            const float MinLevel = 0.0f;
            const float MaxLevel = 1.0f;
            _fadeoutLevel = Math.Clamp(MaxLevel - decrement, MinLevel, MaxLevel);
        }
        
        /// <summary>
        /// Either fades the frame or checks if the channel had any signal output.
        /// Mirrors DOSBox mixer.cpp:2055-2071
        /// </summary>
        public AudioFrame MaybeFadeOrListen(AudioFrame frame) {
            if (_wantsFadeout) {
                // When fading, we actively drive down the channel level
                return frame * _fadeoutLevel;
            }
            
            if (!_hadSignal) {
                // Otherwise, we inspect the running signal for changes
                const float ChangeThreshold = 1.0f;
                
                _hadSignal = Math.Abs(frame.Left - _lastFrame.Left) > ChangeThreshold ||
                            Math.Abs(frame.Right - _lastFrame.Right) > ChangeThreshold;
                
                _lastFrame = frame;
            }
            
            return frame;
        }
        
        /// <summary>
        /// Attempts to put the channel to sleep if conditions are met.
        /// Mirrors DOSBox mixer.cpp:2073-2099
        /// </summary>
        public void MaybeSleep() {
            // A signed integer can hold a duration of ~24 days in milliseconds
            long awakeForMs = Environment.TickCount64 - _wokenAtMs;
            
            // Not enough time has passed... try to sleep later
            if (awakeForMs < _fadeoutOrSleepAfterMs) {
                return;
            }
            
            if (_wantsFadeout) {
                // The channel is still fading out... try to sleep later
                if (_fadeoutLevel > 0.0f) {
                    DecrementFadeLevel((int)awakeForMs);
                    return;
                }
            } else if (_hadSignal) {
                // The channel is still producing a signal... so stay awake
                WakeUp();
                return;
            }
            
            if (_channel.IsEnabled) {
                _channel.Enable(false);
                // LOG_INFO equivalent - commented out to match DOSBox behavior
                // _channel._loggerService.Information("MIXER: {Channel} fell asleep", _channel._name);
            }
        }
        
        /// <summary>
        /// Wakes up the channel.
        /// Mirrors DOSBox mixer.cpp:2101-2119
        /// </summary>
        /// <returns>True when actually awoken, false if already awake</returns>
        public bool WakeUp() {
            // Always reset for another round of awakeness
            _wokenAtMs = Environment.TickCount64;
            _fadeoutLevel = 1.0f;
            _hadSignal = false;
            
            bool wasSleeping = !_channel.IsEnabled;
            if (wasSleeping) {
                _channel.Enable(true);
                // LOG_INFO equivalent - commented out to match DOSBox behavior
                // _channel._loggerService.Information("MIXER: {Channel} woke up", _channel._name);
            }
            
            return wasSleeping;
        }
        
        private static bool HasFalse(string value) {
            return string.Equals(value, "false", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "off", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "0", StringComparison.OrdinalIgnoreCase);
        }
        
        private static bool HasTrue(string value) {
            return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "on", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "1", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Disposes the MixerChannel and its resources.
    /// </summary>
    public void Dispose() {
        _speexResampler?.Dispose();
    }
}
