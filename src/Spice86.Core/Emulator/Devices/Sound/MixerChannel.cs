// SPDX-License-Identifier: GPL-2.0-or-later
namespace Spice86.Core.Emulator.Devices.Sound;

using Spice86.Core.Emulator.Devices.Sound.Blaster;
using Spice86.Shared.Interfaces;

using System.Threading;

using AudioFrame = Spice86.Libs.Sound.Common.AudioFrame;
using HighPass = Spice86.Libs.Sound.Filters.IirFilters.Filters.Butterworth.HighPass;
using LowPass = Spice86.Libs.Sound.Filters.IirFilters.Filters.Butterworth.LowPass;

/// <summary>
/// Represents a single audio channel in the mixer.
/// </summary>
public sealed class MixerChannel {
    private const uint SpeexChannels = 2; // Always use stereo for processing
    private const int SpeexQuality = 5; // Medium quality - good balance between CPU and quality

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

    private AudioFrame _userVolumeGain = new(1.0f, 1.0f);
    private AudioFrame _appVolumeGain = new(1.0f, 1.0f);
    private float _db0VolumeGain = 1.0f;
    private AudioFrame _combinedVolumeGain = new(1.0f, 1.0f);

    private bool _doLerpUpsample;
    private float _lerpPos;
    private float _lerpStep;
    private AudioFrame _lerpLastFrame;

    private bool _doZohUpsample;
    private float _zohStep;
    private float _zohPos;
    private int _zohTargetRateHz;

    // Initialized ONCE when first needed (see ConfigureResampler)
    private Spice86.Libs.Sound.Resampling.SpeexResamplerCSharp? _speexResampler;

    // Pre-allocated resample buffers (avoids per-tick GC allocations)
    private float[] _resampleInputBuffer = Array.Empty<float>();
    private float[] _resampleOutputBuffer = Array.Empty<float>();

    private bool _doResample;

    private ResampleMethod _resampleMethod = ResampleMethod.Resample;

    // Used as intermediate buffer during sample conversion and resampling
    private readonly AudioFrameBuffer _convertBuffer = new(0);

    // Channel mapping
    private StereoLine _outputMap = new() { Left = LineIndex.Left, Right = LineIndex.Right };
    private StereoLine _channelMap = new() { Left = LineIndex.Left, Right = LineIndex.Right };

    // Frame buffers - matches DOSBox audio_frames
    public AudioFrameBuffer AudioFrames { get; } = new(0);

    private AudioFrame _prevFrame = new(0.0f, 0.0f);
    private AudioFrame _nextFrame = new(0.0f, 0.0f);

    // State flags
    public bool IsEnabled { get; private set; }
    private bool _lastSamplesWereStereo;

    // Defines the peak sample amplitude we can expect in this channel.
    // Default to signed 16bit max
    private int _peakAmplitude = 32767; // Max16BitSampleValue

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

    private readonly NoiseGate _noiseGate = new();
    private float _noiseGateThresholdDb = -60.0f;
    private float _noiseGateAttackTimeMs = 1.0f;
    private float _noiseGateReleaseTimeMs = 20.0f;
    private bool _doNoiseGate;

    private readonly HighPass[] _highPassFilters = new HighPass[2] { new(), new() };
    private FilterState _highPassFilterState = FilterState.Off;
    private int _highPassFilterOrder;
    private int _highPassFilterCutoffHz;

    private readonly LowPass[] _lowPassFilters = new LowPass[2] { new(), new() };
    private FilterState _lowPassFilterState = FilterState.Off;
    private int _lowPassFilterOrder;
    private int _lowPassFilterCutoffHz;

    private readonly Sleeper _sleeper;
    private readonly bool _doSleep;

    /// <summary>
    /// Whether this channel uses the sleep feature.
    /// </summary>
    public bool DoSleep => _doSleep;

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

        _doSleep = HasFeature(ChannelFeature.Sleep);
        _sleeper = new Sleeper(this);

        _envelope = new Envelope(name);
    }

    /// <summary>
    /// Gets the channel name.
    /// </summary>
    public string Name {
        get {
            lock (_mutex) {
                return _name;
            }
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
    /// </summary>
    public void SetSampleRate(int sampleRateHz) {
        lock (_mutex) {
            _sampleRateHz = sampleRateHz;

            _envelope.Update(_sampleRateHz,
                _peakAmplitude,
                EnvelopeMaxExpansionOverMs,
                EnvelopeExpiresAfterSeconds);

            if (_doNoiseGate) {
                InitNoiseGate();
            }

            if (_highPassFilterState == FilterState.On) {
                InitHighPassFilter();
            }
            if (_lowPassFilterState == FilterState.On) {
                InitLowPassFilter();
            }

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
    /// </summary>
    public float FramesPerTick {
        get {
            lock (_mutex) {
                float stretchFactor = (float)_sampleRateHz / _mixerSampleRateHz;
                const float MillisPerTick = 1.0f;
                return (_mixerSampleRateHz / 1000.0f) * MillisPerTick * stretchFactor;
            }
        }
    }

    /// <summary>
    /// Gets the number of frames per block.
    /// </summary>
    public float FramesPerBlock {
        get {
            lock (_mutex) {
                float stretchFactor = (float)_sampleRateHz / _mixerSampleRateHz;
                // Assuming default blocksize of 1024 (would need to get from mixer)
                int blocksize = 1024;
                return blocksize * stretchFactor;
            }
        }
    }

    /// <summary>
    /// Gets milliseconds per frame for this channel.
    /// </summary>
    public double MillisPerFrame {
        get {
            lock (_mutex) {
                return 1000.0 / _sampleRateHz;
            }
        }
    }

    /// <summary>
    /// Sets the peak amplitude for this channel.
    /// </summary>
    public void SetPeakAmplitude(int peak) {
        lock (_mutex) {
            _peakAmplitude = peak;
            _envelope.Update(_sampleRateHz,
                _peakAmplitude,
                EnvelopeMaxExpansionOverMs,
                EnvelopeExpiresAfterSeconds);
        }
    }

    private void InitLerpUpsamplerState() {
        if (_sampleRateHz >= _mixerSampleRateHz) {
            throw new InvalidOperationException("LERP upsampler requires channel rate < mixer rate");
        }

        _lerpStep = _sampleRateHz / (float)_mixerSampleRateHz;
        if (_lerpStep >= 1.0f) {
            throw new InvalidOperationException($"LERP step must be < 1.0, got {_lerpStep}");
        }

        _lerpPos = 0.0f;
        _lerpLastFrame = new AudioFrame(0.0f, 0.0f);
    }

    private void InitZohUpsamplerState() {
        if (_sampleRateHz >= _zohTargetRateHz) {
            throw new InvalidOperationException("ZoH upsampler requires channel rate < target rate");
        }

        _zohStep = (float)_sampleRateHz / _zohTargetRateHz;
        _zohPos = 0.0f;
    }

    /// <summary>
    /// Sets the zero-order-hold upsampler target rate.
    /// </summary>
    internal void SetZeroOrderHoldUpsamplerTargetRate(int targetRateHz) {
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
    /// </summary>
    internal void SetResampleMethod(ResampleMethod method) {
        lock (_mutex) {
            _resampleMethod = method;
            ConfigureResampler();
        }
    }

    private void ConfigureResampler() {
        int channelRateHz = _sampleRateHz;
        int mixerRateHz = _mixerSampleRateHz;

        _doLerpUpsample = false;
        _doZohUpsample = false;
        _doResample = false;

        void ConfigureSpeexResampler(int inRateHz) {
            uint speexInRate = (uint)inRateHz;
            uint speexOutRate = (uint)mixerRateHz;

            if (_speexResampler == null) {
                _speexResampler = new Spice86.Libs.Sound.Resampling.SpeexResamplerCSharp(
                    SpeexChannels,
                    speexInRate,
                    speexOutRate,
                    SpeexQuality);
            }

            _speexResampler.SetRate(speexInRate, speexOutRate);

            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Debug)) {
                _loggerService.Debug(
                    "MIXER: {Name}: Speex resampler is on, input rate: {InRate} Hz, output rate: {OutRate} Hz",
                    _name, inRateHz, mixerRateHz);
            }
        }

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
    public HashSet<ChannelFeature> Features {
        get {
            lock (_mutex) {
                return [.. _features];
            }
        }
    }

    /// <summary>
    /// Sets the 0dB scalar for volume normalization.
    /// </summary>
    internal void Set0dbScalar(float scalar) {
        lock (_mutex) {
            _db0VolumeGain = scalar;
            UpdateCombinedVolume();
        }
    }

    /// <summary>
    /// Gets or sets the user volume.
    /// </summary>
    public AudioFrame UserVolume {
        get {
            lock (_mutex) {
                return _userVolumeGain;
            }
        }

        set {
            lock (_mutex) {
                _userVolumeGain = value;
                UpdateCombinedVolume();
            }
        }
    }

    /// <summary>
    /// Gets or sets the application volume (set programmatically by DOS programs).
    /// </summary>
    public AudioFrame AppVolume {
        get {
            lock (_mutex) {
                return _appVolumeGain;
            }
        }

        set {
            lock (_mutex) {
                float clampedLeft = Math.Clamp(value.Left, 0.0f, 1.0f);
                float clampedRight = Math.Clamp(value.Right, 0.0f, 1.0f);
                _appVolumeGain = new AudioFrame(clampedLeft, clampedRight);
                UpdateCombinedVolume();
            }
        }
    }

    private void UpdateCombinedVolume() {
        _combinedVolumeGain = new AudioFrame(
            _userVolumeGain.Left * _appVolumeGain.Left * _db0VolumeGain,
            _userVolumeGain.Right * _appVolumeGain.Right * _db0VolumeGain
        );
    }

    internal void SetChannelMap(StereoLine map) {
        lock (_mutex) {
            _channelMap = map;
        }
    }

    /// <summary>
    /// Gets the channel mapping.
    /// </summary>
    public StereoLine ChannelMap {
        get {
            lock (_mutex) {
                return _channelMap;
            }
        }
    }

    /// <summary>
    /// Gets or sets the output line mapping.
    /// </summary>
    public StereoLine LineoutMap {
        get {
            lock (_mutex) {
                return _outputMap;
            }
        }
        set {
            lock (_mutex) {
                _outputMap = value;
            }
        }
    }

    /// <summary>
    /// Sets the channel settings from a saved configuration.
    /// </summary>
    internal void SetSettings(MixerChannelSettings settings) {
        lock (_mutex) {
            IsEnabled = settings.IsEnabled;
            UserVolume = settings.UserVolumeGain;
            LineoutMap = settings.LineoutMap;

            // Only set effect levels if effects are enabled
            // (would need access to mixer state to check do_crossfeed, do_reverb, do_chorus)
            // For now, always set them
            CrossfeedStrength = settings.CrossfeedStrength;
            ReverbLevel = settings.ReverbLevel;
            ChorusLevel = settings.ChorusLevel;
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
                _framesNeeded = 0;
                AudioFrames.Clear();
                _prevFrame = new AudioFrame(0.0f, 0.0f);
                _nextFrame = new AudioFrame(0.0f, 0.0f);

                ClearResampler();
            }

            IsEnabled = shouldEnable;
        }
    }

    private void ClearResampler() {
        if (_doLerpUpsample) {
            InitLerpUpsamplerState();
        }
        if (_doZohUpsample) {
            InitZohUpsamplerState();
        }
        if (_doResample && _speexResampler != null) {
            // Reset Speex resampler memory and skip zeros
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
    /// </summary>
    internal void Mix(int framesRequested) {
        if (framesRequested <= 0) {
            throw new ArgumentException("Frames requested must be positive", nameof(framesRequested));
        }

        if (!IsEnabled) {
            return;
        }

        _framesNeeded = framesRequested;

        // Simple loop that calls handler until we have enough frames
        while (_framesNeeded > AudioFrames.Count) {
            int framesRemaining;
            lock (_mutex) {
                // Calculate stretch factor based on sample rates
                float stretchFactor = _sampleRateHz / (float)_mixerSampleRateHz;

                // Calculate how many frames we still need
                framesRemaining = (int)Math.Ceiling(
                    (_framesNeeded - AudioFrames.Count) * stretchFactor);

                if (framesRemaining <= 0) {
                    break;
                }
            }

            _handler(framesRemaining);
        }
    }

    /// <summary>
    /// Gets or sets the crossfeed strength for this channel.
    /// </summary>
    public float CrossfeedStrength {
        get {
            lock (_mutex) {
                return _crossfeedStrength;
            }
        }
        set {
            lock (_mutex) {
                _crossfeedStrength = Math.Clamp(value, 0.0f, 1.0f);
                _doCrossfeed = HasFeature(ChannelFeature.Stereo) && _crossfeedStrength > 0.0f;
                if (!_doCrossfeed) {
                    _crossfeedStrength = 0.0f;
                    return;
                }
                float p = (1.0f - _crossfeedStrength) / 2.0f;
                const float center = 0.5f;
                _crossfeedPanLeft = center - p;
                _crossfeedPanRight = center + p;
            }
        }
    }

    /// <summary>
    /// Gets or sets the reverb send level for this channel.
    /// </summary>
    public float ReverbLevel {
        get {
            lock (_mutex) {
                return _reverbLevel;
            }
        }
        set {
            lock (_mutex) {
                const float levelMin = 0.0f;
                const float levelMax = 1.0f;
                const float levelMinDb = -40.0f;
                const float levelMaxDb = 0.0f;

                value = Math.Clamp(value, levelMin, levelMax);
                _doReverbSend = HasFeature(ChannelFeature.ReverbSend) && value > levelMin;
                if (!_doReverbSend) {
                    _reverbLevel = levelMin;
                    _reverbSendGain = 0.0f;
                    return;
                }
                _reverbLevel = value;
                // Remap level to decibels and convert to gain
                float levelDb = Remap(levelMin, levelMax, levelMinDb, levelMaxDb, value);
                _reverbSendGain = DecibelToGain(levelDb);
            }
        }
    }

    /// <summary>
    /// Gets or sets the chorus send level for this channel.
    /// </summary>
    public float ChorusLevel {
        get {
            lock (_mutex) {
                return _chorusLevel;
            }
        }

        set {
            lock (_mutex) {
                const float levelMin = 0.0f;
                const float levelMax = 1.0f;
                const float levelMinDb = -24.0f;
                const float levelMaxDb = 0.0f;

                value = Math.Clamp(value, levelMin, levelMax);
                _doChorusSend = HasFeature(ChannelFeature.ChorusSend) && value > levelMin;

                if (!_doChorusSend) {
                    _chorusLevel = levelMin;
                    _chorusSendGain = 0.0f;
                    return;
                }

                _chorusLevel = value;

                // Remap level to decibels and convert to gain
                float levelDb = Remap(levelMin, levelMax, levelMinDb, levelMaxDb, value);
                _chorusSendGain = DecibelToGain(levelDb);
            }
        }
    }

    /// <summary>
    /// Configures the noise gate with operating parameters.
    /// </summary>
    /// <param name="thresholdDb">Threshold in dB below which signal is gated</param>
    /// <param name="attackTimeMs">Attack time in milliseconds</param>
    /// <param name="releaseTimeMs">Release time in milliseconds</param>
    internal void ConfigureNoiseGate(float thresholdDb, float attackTimeMs, float releaseTimeMs) {
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
    /// </summary>
    /// <param name="enabled">True to enable, false to disable</param>
    internal void EnableNoiseGate(bool enabled) {
        lock (_mutex) {
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
                _loggerService.Information("{Channel}: Noise gate {State}",
                    _name, enabled ? "enabled" : "disabled");
            }
            _doNoiseGate = enabled;
        }
    }

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

    /// <summary>
    /// Gets or sets the state of the high-pass filter.
    /// </summary>
    public FilterState HighPassFilter {
        get {
            lock (_mutex) {
                return _highPassFilterState;
            }
        }
        set {
            lock (_mutex) {
                _highPassFilterState = value;
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
    }

    /// <summary>
    /// Gets or sets the state of the low-pass filter.
    /// </summary>
    public FilterState LowPassFilter {
        get {
            lock (_mutex) {
                return _lowPassFilterState;
            }
        }
        set {
            lock (_mutex) {
                _lowPassFilterState = value;

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
    }

    /// <summary>
    /// Configures the high-pass filter parameters.
    /// </summary>
    /// <param name="order">Filter order (1-16)</param>
    /// <param name="cutoffFreqHz">Cutoff frequency in Hz</param>
    internal void ConfigureHighPassFilter(int order, int cutoffFreqHz) {
        const int MaxFilterOrder = 16;

        if (order is <= 0 or > MaxFilterOrder) {
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

    private void InitHighPassFilter() {
        const int MaxFilterOrder = 16;

        if (_highPassFilterOrder is <= 0 or > MaxFilterOrder) {
            throw new InvalidOperationException("High-pass filter order is invalid");
        }
        if (_highPassFilterCutoffHz <= 0) {
            throw new InvalidOperationException("High-pass filter cutoff frequency is invalid");
        }

        lock (_mutex) {
            foreach (HighPass filter in _highPassFilters) {
                filter.Setup(_highPassFilterOrder, _mixerSampleRateHz, _highPassFilterCutoffHz);
            }
        }
    }

    /// <summary>
    /// Configures the low-pass filter parameters.
    /// </summary>
    /// <param name="order">Filter order (1-16)</param>
    /// <param name="cutoffFreqHz">Cutoff frequency in Hz</param>
    internal void ConfigureLowPassFilter(int order, int cutoffFreqHz) {
        const int MaxFilterOrder = 16;

        if (order is <= 0 or > MaxFilterOrder) {
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

    private void InitLowPassFilter() {
        const int MaxFilterOrder = 16;

        if (_lowPassFilterOrder is <= 0 or > MaxFilterOrder) {
            throw new InvalidOperationException("Low-pass filter order is invalid");
        }
        if (_lowPassFilterCutoffHz <= 0) {
            throw new InvalidOperationException("Low-pass filter cutoff frequency is invalid");
        }

        lock (_mutex) {
            foreach (LowPass filter in _lowPassFilters) {
                filter.Setup(_lowPassFilterOrder, _mixerSampleRateHz, _lowPassFilterCutoffHz);
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
            if (AudioFrames.Count < _framesNeeded) {
                if (_prevFrame.Left == 0.0f && _prevFrame.Right == 0.0f) {
                    // Pure silence - just add zero frames
                    while (AudioFrames.Count < _framesNeeded) {
                        AudioFrames.Add(new AudioFrame(0.0f, 0.0f));
                    }
                    // Make sure the next samples are zero when they get
                    // switched to prev
                    _nextFrame = new AudioFrame(0.0f, 0.0f);

                } else {
                    // Fade to silence to avoid clicks
                    bool stereo = _lastSamplesWereStereo;

                    LineIndex mappedOutputLeft = _outputMap.Left;
                    LineIndex mappedOutputRight = _outputMap.Right;

                    while (AudioFrames.Count < _framesNeeded) {
                        // Fade gradually to silence to avoid clicks.
                        // Maybe the fade factor f depends on the sample rate.
                        const float f = 4.0f;

                        for (int ch = 0; ch < 2; ch++) {
                            if (_prevFrame[ch] > f) {
                                _nextFrame[ch] = _prevFrame[ch] - f;
                            } else if (_prevFrame[ch] < -f) {
                                _nextFrame[ch] = _prevFrame[ch] + f;
                            } else {
                                _nextFrame[ch] = 0.0f;
                            }
                        }
                        AudioFrame frameWithGain = (stereo ? _prevFrame : new AudioFrame(_prevFrame.Left)) * _combinedVolumeGain;
                        AudioFrame outFrame = new();
                        outFrame[(int)mappedOutputLeft] = frameWithGain.Left;
                        outFrame[(int)mappedOutputRight] = frameWithGain.Right;
                        AudioFrames.Add(outFrame);
                        _prevFrame = _nextFrame;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Converts mono 8-bit unsigned samples to AudioFrames with optional ZoH upsampling.
    /// Fills _convertBuffer with converted and potentially ZoH-upsampled frames.
    /// </summary>
    private void ConvertSamplesAndMaybeZohUpsampleBytes(ReadOnlySpan<byte> data, int numFrames) {
        _convertBuffer.Clear();

        LineIndex mappedOutputLeft = _outputMap.Left;
        LineIndex mappedOutputRight = _outputMap.Right;
        LineIndex mappedChannelLeft = _channelMap.Left;

        int pos = 0;
        while (pos < numFrames) {
            _prevFrame = _nextFrame;

            // Convert 8-bit unsigned to float
            _nextFrame = new AudioFrame(LookupTables.U8To16[data[pos]], LookupTables.U8To16[data[pos]]);

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
    /// Fills _convertBuffer with converted and potentially ZoH-upsampled frames.
    /// </summary>
    private void ConvertSamplesAndMaybeZohUpsampleShorts(ReadOnlySpan<short> data, int numFrames) {
        _convertBuffer.Clear();

        LineIndex mappedOutputLeft = _outputMap.Left;
        LineIndex mappedOutputRight = _outputMap.Right;
        LineIndex mappedChannelLeft = _channelMap.Left;

        int pos = 0;
        while (pos < numFrames) {
            _prevFrame = _nextFrame;

            // Convert 16-bit signed to float
            _nextFrame = new AudioFrame(data[pos], data[pos]);

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

            // Apply channel mapping (stereo) - use _prevFrame (DOSBox pattern)
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
    /// Reference: DOSBox staging mixer.cpp ConvertSamplesAndMaybeZohUpsample()
    /// Float samples are expected to be in int16 range already (like DOSBox staging).
    /// </summary>
    private void ConvertSamplesAndMaybeZohUpsample_mfloat(ReadOnlySpan<float> data, int numFrames) {
        _convertBuffer.Clear();

        LineIndex mappedOutputLeft = _outputMap.Left;
        LineIndex mappedOutputRight = _outputMap.Right;
        LineIndex mappedChannelLeft = _channelMap.Left;

        int pos = 0;
        while (pos < numFrames) {
            _prevFrame = _nextFrame;

            // Float samples are already in int16 range (like DOSBox staging)
            // Reference: DOSBox mixer.cpp just does static_cast<float>(data[pos]) with no conversion
            float sample = data[pos];
            _nextFrame = new AudioFrame(sample, sample);

            // Apply channel mapping (mono uses left channel) - use _prevFrame (DOSBox pattern)
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
            float left = data[pos * 2];
            float right = data[pos * 2 + 1];
            _nextFrame = new AudioFrame(left, right);

            // Apply channel mapping (stereo) - use _prevFrame (DOSBox pattern)
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
    /// Linear interpolation helper function.
    /// </summary>
    private static float Lerp(float a, float b, float t) {
        return a * (1.0f - t) + b * t;
    }

    /// <summary>
    /// Applies Speex resampling to convert_buffer → audio_frames.
    /// </summary>
    private void ApplySpeexResampling(int audioFramesStartingSize) {
        if(_speexResampler is null or { IsInitialized: false}) {
            throw new InvalidOperationException("Speex Resampler was null or not initialized before audio resampling");
        }
        int inFrames = _convertBuffer.Count;
        if (inFrames == 0) {
            return;
        }

        int estimatedOutFrames = EstimateMaxOutFrames(_speexResampler, inFrames);

        // Resize audio_frames to accommodate new frames
        int targetSize = audioFramesStartingSize + estimatedOutFrames;
        AudioFrames.Resize(targetSize);

        // Prepare input buffer - convert AudioFrame[] to interleaved float[]
        int inputSize = inFrames * 2;
        if (_resampleInputBuffer.Length < inputSize) {
            _resampleInputBuffer = new float[inputSize];
        }
        Span<AudioFrame> convertSpan = _convertBuffer.AsSpan();
        for (int i = 0; i < inFrames; i++) {
            _resampleInputBuffer[i * 2] = convertSpan[i].Left;
            _resampleInputBuffer[i * 2 + 1] = convertSpan[i].Right;
        }

        // Prepare output buffer
        int outputSize = estimatedOutFrames * 2;
        if (_resampleOutputBuffer.Length < outputSize) {
            _resampleOutputBuffer = new float[outputSize];
        }

        // Process through Speex resampler (interleaved stereo)
        _speexResampler.ProcessInterleavedFloat(
            _resampleInputBuffer.AsSpan(0, inputSize),
            _resampleOutputBuffer.AsSpan(0, outputSize),
            out uint _,
            out uint outFramesGenerated);

        // Copy resampled frames back to audio_frames
        for (int i = 0; i < (int)outFramesGenerated; i++) {
            AudioFrames[audioFramesStartingSize + i] = new AudioFrame(
                _resampleOutputBuffer[i * 2],
                _resampleOutputBuffer[i * 2 + 1]);
        }

        // Trim audio_frames to actual size
        int actualSize = audioFramesStartingSize + (int)outFramesGenerated;
        if (AudioFrames.Count > actualSize) {
            AudioFrames.RemoveRange(actualSize, AudioFrames.Count - actualSize);
        }
    }

    private static int EstimateMaxOutFrames(Spice86.Libs.Sound.Resampling.SpeexResamplerCSharp resampler, int inFrames) {
        resampler.GetRatio(out uint ratioNum, out uint ratioDen);
        if (ratioNum == 0 || ratioDen == 0 || inFrames <= 0) {
            return inFrames;
        }

        double numerator = (double)inFrames * ratioDen;
        int estimated = (int)Math.Ceiling(numerator / ratioNum);
        return estimated <= 0 ? inFrames : estimated;
    }

    /// <summary>
    /// Applies in-place processing (filters, crossfeed) to newly added frames.
    /// </summary>
    private void ApplyInPlaceProcessing(int startIndex) {
        // Optionally gate, filter, and apply crossfeed.
        // Runs in-place over newly added frames.
        for (int i = startIndex; i < AudioFrames.Count; i++) {
            if (_doNoiseGate) {
                AudioFrames[i] = _noiseGate.Process(AudioFrames[i]);
            }

            if (_highPassFilterState == FilterState.On) {
                AudioFrames[i] = new AudioFrame(
                    _highPassFilters[0].Filter(AudioFrames[i].Left),
                    _highPassFilters[1].Filter(AudioFrames[i].Right)
                );
            }

            if (_lowPassFilterState == FilterState.On) {
                AudioFrames[i] = new AudioFrame(
                    _lowPassFilters[0].Filter(AudioFrames[i].Left),
                    _lowPassFilters[1].Filter(AudioFrames[i].Right)
                );
            }

            if (_doCrossfeed) {
                AudioFrames[i] = ApplyCrossfeed(AudioFrames[i]);
            }
        }
    }

    /// <summary>
    /// Applies crossfeed to a single frame.
    /// </summary>
    private AudioFrame ApplyCrossfeed(AudioFrame frame) {
        // Pan mono sample using -6dB linear pan law in the stereo field
        // pan: 0.0 = left, 0.5 = center, 1.0 = right
        static AudioFrame PanSample(float sample, float pan) {
            return new AudioFrame((1.0f - pan) * sample, pan * sample);
        }

        AudioFrame a = PanSample(frame.Left, _crossfeedPanLeft);
        AudioFrame b = PanSample(frame.Right, _crossfeedPanRight);
        return new AudioFrame(a.Left + b.Left, a.Right + b.Right);
    }

    /// <summary>
    /// Adds stereo 32-bit float samples with resampling.
    /// </summary>
    public void AddSamplesFloat(int numFrames, ReadOnlySpan<float> data) {
        if (numFrames <= 0) {
            return;
        }
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Debug)) {
            _loggerService.Debug("MIXER_CHANNEL: {Channel}: AddSamples_mfloat frames={Frames} doResample={DoResample} doLerp={DoLerp} doZoh={DoZoh}",
                _name, numFrames, _doResample, _doLerpUpsample, _doZohUpsample);
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

                    AudioFrame lerpedFrame = new AudioFrame(
                        Lerp(_lerpLastFrame.Left, currFrame.Left, _lerpPos),
                        Lerp(_lerpLastFrame.Right, currFrame.Right, _lerpPos)
                    );

                    AudioFrames.Add(lerpedFrame);

                    _lerpPos += _lerpStep;

                    if (_lerpPos > 1.0f) {
                        _lerpPos -= 1.0f;
                        _lerpLastFrame = currFrame;
                        i++;
                    }
                }
            } else if (_doResample) {
                // Speex resampling
                ApplySpeexResampling(audioFramesStartingSize);
            } else {
                // No resampling
                AudioFrames.AddRange(_convertBuffer.AsSpan());
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

                    AudioFrame lerpedFrame = new AudioFrame(
                        Lerp(_lerpLastFrame.Left, currFrame.Left, _lerpPos),
                        Lerp(_lerpLastFrame.Right, currFrame.Right, _lerpPos)
                    );

                    AudioFrames.Add(lerpedFrame);

                    _lerpPos += _lerpStep;

                    if (_lerpPos > 1.0f) {
                        _lerpPos -= 1.0f;
                        _lerpLastFrame = currFrame;
                        i++;
                    }
                }
            } else if (_doResample) {
                // Speex resampling
                ApplySpeexResampling(audioFramesStartingSize);
            } else {
                // No resampling
                AudioFrames.AddRange(_convertBuffer.AsSpan());
            }

            // Apply in-place processing to newly added frames
            ApplyInPlaceProcessing(audioFramesStartingSize);
        }
    }

    /// <summary>
    /// Applies fade-out or signal detection to a frame if sleep is enabled.
    /// Called by mixer during frame processing.
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
    /// Nested class that manages channel sleep/wake behavior with optional fade-out.
    /// </summary>
    private sealed class Sleeper {
        private readonly MixerChannel _channel;

        // The wait before fading or sleeping is bound between these values
        private const int MinWaitMs = 100;
        private const int DefaultWaitMs = 500;
        private const int MaxWaitMs = 5000;

        private AudioFrame _lastFrame;
        private long _wokenAtMs;
        private readonly int _fadeoutOrSleepAfterMs;
        private bool _hadSignal;

        public Sleeper(MixerChannel channel, int sleepAfterMs = DefaultWaitMs) {
            _channel = channel ?? throw new ArgumentNullException(nameof(channel));
            _fadeoutOrSleepAfterMs = sleepAfterMs;

            // The constructed sleep period is programmatically controlled
            if (sleepAfterMs is < MinWaitMs or > MaxWaitMs) {
                throw new ArgumentOutOfRangeException(nameof(sleepAfterMs),
                    $"Sleep period must be between {MinWaitMs} and {MaxWaitMs} ms");
            }
        }

        /// <summary>
        /// Either fades the frame or checks if the channel had any signal output.
        /// </summary>
        public AudioFrame MaybeFadeOrListen(AudioFrame frame) {
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
        /// </summary>
        public void MaybeSleep() {
            // A signed integer can hold a duration of ~24 days in milliseconds
            long awakeForMs = Environment.TickCount64 - _wokenAtMs;

            // Not enough time has passed... try to sleep later
            if (awakeForMs < _fadeoutOrSleepAfterMs) {
                return;
            }

            if (_hadSignal) {
                // The channel is still producing a signal... so stay awake
                WakeUp();
                return;
            }

            if (_channel.IsEnabled) {
                _channel.Enable(false);
            }
        }

        /// <summary>
        /// Wakes up the channel.
        /// </summary>
        /// <returns>True when actually awoken, false if already awake</returns>
        public bool WakeUp() {
            // Always reset for another round of awakeness
            _wokenAtMs = Environment.TickCount64;
            _hadSignal = false;

            bool wasSleeping = !_channel.IsEnabled;
            if (wasSleeping) {
                _channel.Enable(true);
            }

            return wasSleeping;
        }
    }
}
