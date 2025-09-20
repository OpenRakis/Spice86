namespace Spice86.Core.Emulator.Devices.Sound;

using Spice86.Core.Backend.Audio.IirFilters;

/// <summary>
/// Represents a sound channel with DOSBox-compatible mixer functionality.
/// </summary>
public class SoundChannel {
    private readonly SoftwareMixer _mixer;
    private readonly HashSet<ChannelFeature> _features = new();
    private readonly Action<int>? _handler;
    
    // Audio processing using Span<T> for performance
    private readonly CircularBuffer<float> _audioBuffer = new(8192); // stereo interleaved
    private int _sampleRateHz = 49716; // OPL2 native rate
    private float _appVolumeLeft = 1.0f;
    private float _appVolumeRight = 1.0f;
    private float _db0VolumeGain = 1.5f; // DOSBox OPL gain
    private bool _isEnabled = true;
    
    // DOSBox mixer effects - no locks needed since DeviceThread handles synchronization
    private float _crossfeedStrength;
    private bool _doCrossfeed;
    
    private float _reverbLevel;
    private float _reverbSendGain;
    private bool _doReverbSend;
    
    private float _chorusLevel;
    private float _chorusSendGain;
    private bool _doChorusSend;
    
    // Noise gate for OPL2 residual noise removal
    private float _noiseGateThresholdDb = -65.0f;
    private bool _doNoiseGate;
    
    // Filters using existing IIR filters
    private readonly LowPass[] _highpassFilter = new LowPass[2];
    private readonly LowPass[] _lowpassFilter = new LowPass[2];
    private readonly FilterState _highpassState = FilterState.Off;
    private readonly FilterState _lowpassState = FilterState.Off;
    
    // Sleep/fadeout for OPL2
    private readonly bool _doSleep;
    private float _fadeoutLevel = 1.0f;
    private long _wokenAtMs;
    private bool _hadSignal;

    /// <summary>
    /// Initializes a new instance of the <see cref="SoundChannel"/> class.
    /// </summary>
    /// <param name="mixer">The software mixer to register the sound channel with.</param>
    /// <param name="name">The name of the sound channel.</param>
    public SoundChannel(SoftwareMixer mixer, string name) {
        _mixer = mixer;
        Name = name;
        
        // Initialize filters
        for (int i = 0; i < 2; i++) {
            _highpassFilter[i] = new LowPass();
            _lowpassFilter[i] = new LowPass();
        }
        
        mixer.Register(this);
    }
    
    /// <summary>
    /// DOSBox-compatible constructor for mixer channels with features and handler.
    /// </summary>
    public SoundChannel(SoftwareMixer mixer, string name, HashSet<ChannelFeature> features, Action<int> handler) {
        _mixer = mixer;
        Name = name;
        _features = features;
        _handler = handler;
        _doSleep = _features.Contains(ChannelFeature.Sleep);
        
        // Initialize filters
        for (int i = 0; i < 2; i++) {
            _highpassFilter[i] = new LowPass();
            _lowpassFilter[i] = new LowPass();
        }
        
        mixer.Register(this);
    }

    /// <summary>
    /// Renders the audio frame to the sound channel.
    /// </summary>
    public int Render(Span<float> data) {
        return _mixer.Render(data, this);
    }
    
    /// <summary>
    /// Renders the audio frame to the sound channel.
    /// </summary>
    public int Render(Span<short> data) {
        return _mixer.Render(data, this);
    }
    
    /// <summary>
    /// Renders the audio frame to the sound channel.
    /// </summary>
    public int Render(Span<byte> data) {
        return _mixer.Render(data, this);
    }

    /// <summary>
    /// Gets or sets the stereo separation, as a percentage.
    /// </summary>
    public float StereoSeparation { get; set; } = 50;

    /// <summary>
    /// Gets the name of the sound channel.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets or sets a value indicating whether the sound channel is muted.
    /// </summary>
    public bool IsMuted { get; set; }

    private int _volume = 100;

    /// <summary>
    /// Gets or sets the volume, as a percentage.
    /// </summary>
    public int Volume {
        get => _volume;
        set => _volume = Math.Clamp(value, 0, 100);
    }
    
    // DOSBox-compatible mixer methods
    
    /// <summary>
    /// Checks if the channel has a specific feature.
    /// </summary>
    public bool HasFeature(ChannelFeature feature) => _features.Contains(feature);
    
    /// <summary>
    /// Gets the channel's sample rate.
    /// </summary>
    public int GetSampleRate() => _sampleRateHz;
    
    /// <summary>
    /// Sets the channel sample rate.
    /// </summary>
    public void SetSampleRate(int sampleRateHz) {
        _sampleRateHz = sampleRateHz;
    }
    
    /// <summary>
    /// Sets the 0dB scalar for volume normalization.
    /// </summary>
    public void Set0dbScalar(float scalar) {
        _db0VolumeGain = scalar;
    }
    
    /// <summary>
    /// Gets the application volume levels (no tuples, use out parameters).
    /// </summary>
    public void GetAppVolume(out float left, out float right) {
        left = _appVolumeLeft;
        right = _appVolumeRight;
    }
    
    /// <summary>
    /// Sets the application volume levels (for HardwareMixer integration).
    /// </summary>
    public void SetAppVolume(float left, float right) {
        _appVolumeLeft = Math.Clamp(left, 0.0f, 1.0f);
        _appVolumeRight = Math.Clamp(right, 0.0f, 1.0f);
    }
    
    /// <summary>
    /// Enables or disables the channel.
    /// </summary>
    public void Enable(bool shouldEnable) {
        if (_isEnabled == shouldEnable) return;
        
        if (!shouldEnable) {
            _audioBuffer.Clear();
        }
        _isEnabled = shouldEnable;
    }
    
    /// <summary>
    /// Wakes up the channel if it supports sleep.
    /// </summary>
    public bool WakeUp() {
        if (!_doSleep) return false;
        
        _wokenAtMs = Environment.TickCount64;
        _fadeoutLevel = 1.0f;
        _hadSignal = false;

        bool wasSleeping = !_isEnabled;
        if (wasSleeping) {
            Enable(true);
        }
        return wasSleeping;
    }
    
    /// <summary>
    /// Configures the noise gate for OPL2 residual noise removal.
    /// </summary>
    public void ConfigureNoiseGate(float thresholdDb, float attackTimeMs, float releaseTimeMs) {
        _noiseGateThresholdDb = thresholdDb;
    }
    
    /// <summary>
    /// Enables or disables the noise gate.
    /// </summary>
    public void EnableNoiseGate(bool enabled) {
        _doNoiseGate = enabled;
    }
    
    /// <summary>
    /// Sets the crossfeed strength for stereo separation reduction.
    /// </summary>
    public void SetCrossfeedStrength(float strength) {
        _doCrossfeed = HasFeature(ChannelFeature.Stereo) && strength > 0.0f;
        _crossfeedStrength = strength;
    }
    
    /// <summary>
    /// Gets the crossfeed strength.
    /// </summary>
    public float GetCrossfeedStrength() => _crossfeedStrength;
    
    /// <summary>
    /// Sets the reverb level.
    /// </summary>
    public void SetReverbLevel(float level) {
        _doReverbSend = HasFeature(ChannelFeature.ReverbSend) && level > 0.0f;
        
        if (!_doReverbSend) {
            _reverbLevel = 0.0f;
            _reverbSendGain = 0.0f;
            return;
        }
        
        _reverbLevel = level;
        _reverbSendGain = (float)Math.Pow(10.0, (-40.0f + level * 40.0f) / 20.0);
    }
    
    /// <summary>
    /// Gets the reverb level.
    /// </summary>
    public float GetReverbLevel() => _reverbLevel;
    
    /// <summary>
    /// Sets the chorus level.
    /// </summary>
    public void SetChorusLevel(float level) {
        _doChorusSend = HasFeature(ChannelFeature.ChorusSend) && level > 0.0f;
        
        if (!_doChorusSend) {
            _chorusLevel = 0.0f;
            _chorusSendGain = 0.0f;
            return;
        }
        
        _chorusLevel = level;
        _chorusSendGain = (float)Math.Pow(10.0, (-24.0f + level * 24.0f) / 20.0);
    }
    
    /// <summary>
    /// Gets the chorus level.
    /// </summary>
    public float GetChorusLevel() => _chorusLevel;
    
    /// <summary>
    /// DOSBox-compatible AddSamples_sfloat method for stereo float data.
    /// Uses Span for performance, no locks needed due to DeviceThread architecture.
    /// </summary>
    public void AddSamples_sfloat(int numFrames, ReadOnlySpan<float> data) {
        if (numFrames <= 0 || !_isEnabled) return;
        
        // Process audio using Span<T> for performance
        Span<float> processedData = stackalloc float[numFrames * 2];
        
        for (int i = 0; i < numFrames * 2; i += 2) {
            float left = data[i] * _appVolumeLeft * _db0VolumeGain;
            float right = data[i + 1] * _appVolumeRight * _db0VolumeGain;
            
            // Apply noise gate (simplified for OPL2 residual noise)
            if (_doNoiseGate) {
                double magnitude = Math.Sqrt(left * left + right * right);
                float thresholdLinear = (float)Math.Pow(10.0, _noiseGateThresholdDb / 20.0);
                if (magnitude < thresholdLinear) {
                    left *= 0.1f;
                    right *= 0.1f;
                }
            }
            
            // Apply filters using existing IIR filters
            if (_highpassState == FilterState.On) {
                left = (float)_highpassFilter[0].Filter(left);
                right = (float)_highpassFilter[1].Filter(right);
            }
            
            if (_lowpassState == FilterState.On) {
                left = (float)_lowpassFilter[0].Filter(left);
                right = (float)_lowpassFilter[1].Filter(right);
            }
            
            // Apply crossfeed for OPL2 stereo (DualOPL2)
            if (_doCrossfeed) {
                float crossfeedAmount = _crossfeedStrength * 0.4f; // DOSBox normal strength
                float newLeft = left * (1.0f - crossfeedAmount) + right * crossfeedAmount;
                float newRight = right * (1.0f - crossfeedAmount) + left * crossfeedAmount;
                left = newLeft;
                right = newRight;
            }
            
            // Handle sleep/fadeout for OPL2
            if (_doSleep && _fadeoutLevel < 1.0f) {
                left *= _fadeoutLevel;
                right *= _fadeoutLevel;
            }
            
            processedData[i] = left;
            processedData[i + 1] = right;
        }
        
        // Add to circular buffer (no locks needed, single producer/consumer)
        _audioBuffer.AddRange(processedData);
    }
    
    /// <summary>
    /// Requests audio frames for mixing.
    /// </summary>
    public void Mix(int framesRequested) {
        if (!_isEnabled || _handler == null) return;

        int samplesNeeded = framesRequested * 2; // stereo
        
        while (_audioBuffer.Count < samplesNeeded) {
            float stretchFactor = (float)_sampleRateHz / 48000.0f;
            int framesRemaining = (int)Math.Ceiling((samplesNeeded - _audioBuffer.Count) / 2 * stretchFactor);
            
            if (framesRemaining <= 0) break;
            
            _handler(framesRemaining);
        }
    }
    
    /// <summary>
    /// Gets audio frames for the mixer (DOSBox-compatible).
    /// Uses Span for performance.
    /// </summary>
    public int GetAudioFrames(Span<float> output) {
        int framesCopied = 0;
        int maxFrames = Math.Min(output.Length / 2, _audioBuffer.Count / 2);
        
        for (int i = 0; i < maxFrames; i++) {
            if (_audioBuffer.Count >= 2) {
                output[i * 2] = _audioBuffer.Dequeue();     // left
                output[i * 2 + 1] = _audioBuffer.Dequeue(); // right
                framesCopied++;
            } else {
                break;
            }
        }
        
        return framesCopied;
    }
    
    /// <summary>
    /// Adds silence frames (DOSBox-compatible).
    /// </summary>
    public void AddSilence() {
        // Add silence frames as needed for OPL2
        Span<float> silence = stackalloc float[192]; // ~2ms at 48kHz, stereo
        silence.Clear();
        _audioBuffer.AddRange(silence);
    }
    
    /// <summary>
    /// Configures fade-out for hanging notes.
    /// </summary>
    public bool ConfigureFadeOut(string prefs) {
        if (prefs is "off" or "false") {
            return true;
        }
        
        if (prefs is "fade" or "true") {
            return true;
        }
        
        // Custom format parsing would go here
        return false;
    }
    
    /// <summary>
    /// Sets the resample method (simplified for OPL2).
    /// </summary>
    public void SetResampleMethod(ResampleMethod method) {
        // OPL2 typically runs at its native rate, minimal resampling needed
    }
}

/// <summary>
/// Channel features for DOSBox mixer compatibility.
/// </summary>
public enum ChannelFeature {
    Sleep,
    FadeOut,
    NoiseGate,
    ReverbSend,
    ChorusSend,
    Synthesizer,
    Stereo,
    DigitalAudio
}

/// <summary>
/// Filter states for audio processing.
/// </summary>
public enum FilterState { Off, On }

/// <summary>
/// Resample methods for DOSBox compatibility.
/// </summary>
public enum ResampleMethod {
    LerpUpsampleOrResample,
    ZeroOrderHoldAndResample,
    Resample
}

/// <summary>
/// High-performance circular buffer using Span.
/// No locks needed due to single producer/consumer pattern in DeviceThread architecture.
/// </summary>
internal class CircularBuffer<T> {
    private readonly T[] _buffer;
    private int _head;
    private int _tail;
    private int _count;

    public CircularBuffer(int capacity) {
        _buffer = new T[capacity];
    }

    public int Count => _count;

    public void AddRange(ReadOnlySpan<T> items) {
        foreach (T? item in items) {
            if (_count < _buffer.Length) {
                _buffer[_head] = item;
                _head = (_head + 1) % _buffer.Length;
                _count++;
            }
        }
    }

    public T Dequeue() {
        if (_count == 0) return default(T)!;

        T? item = _buffer[_tail];
        _tail = (_tail + 1) % _buffer.Length;
        _count--;
        return item;
    }

    public void Clear() {
        _head = 0;
        _tail = 0;
        _count = 0;
    }
}
