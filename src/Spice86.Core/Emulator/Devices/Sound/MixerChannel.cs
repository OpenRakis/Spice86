// SPDX-License-Identifier: GPL-2.0-or-later
// MixerChannel implementation mirrored from DOSBox Staging
// Reference: src/audio/mixer.h and mixer.cpp

namespace Spice86.Core.Emulator.Devices.Sound;

using Spice86.Core.Emulator.Devices.Sound.Blaster;
using Spice86.Libs.Sound.Common;
using Spice86.Shared.Interfaces;

/// <summary>
/// Represents a single audio channel in the mixer.
/// Mirrors DOSBox Staging's MixerChannel class.
/// </summary>
public sealed class MixerChannel : IDisposable {
    private const uint SpeexChannels = 2; // Always use stereo for processing
    private const int SpeexQuality = 5; // Medium quality - good balance between CPU and quality
    
    private readonly Action<int> _handler;
    private readonly string _name;
    private readonly HashSet<ChannelFeature> _features;
    private readonly ILoggerService _loggerService;
    private readonly object _mutex = new();

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
    // Replaces P/Invoke version with faithful C# port
    // Initialized once and reused throughout the channel's lifetime
    private readonly Bufdio.Spice86.SpeexResamplerCSharp _speexResampler;
    
    // Resample method - mirrors DOSBox resample_method
    private ResampleMethod _resampleMethod = ResampleMethod.LerpUpsampleOrResample;

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
    
    // Sleep/wake state - mirrors DOSBox sleeper
    private readonly Sleeper _sleeper;
    private bool _doSleep;

    public MixerChannel(
        Action<int> handler,
        string name,
        HashSet<ChannelFeature> features,
        ILoggerService loggerService) {
        
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _name = name ?? throw new ArgumentNullException(nameof(name));
        _features = features ?? throw new ArgumentNullException(nameof(features));
        _loggerService = loggerService ?? throw new ArgumentNullException(nameof(loggerService));
        
        // Initialize Speex resampler - will be configured with correct rates when SetSampleRate is called
        // Using default rates initially (will be updated in ConfigureResampler)
        _speexResampler = new Bufdio.Spice86.SpeexResamplerCSharp(
            SpeexChannels,
            (uint)_sampleRateHz,
            (uint)_mixerSampleRateHz,
            SpeexQuality);
        
        // Initialize sleep/wake mechanism - mirrors DOSBox mixer.cpp:313
        _doSleep = HasFeature(ChannelFeature.Sleep);
        _sleeper = new Sleeper(this);
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
            
            // Configure resampling if channel rate differs from mixer rate
            // Mirrors DOSBox resample configuration logic
            if (_sampleRateHz < _mixerSampleRateHz) {
                _doLerpUpsample = true;
                InitLerpUpsamplerState();
            } else {
                _doLerpUpsample = false;
            }
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
    /// Mirrors DOSBox ConfigureResampler() from mixer.cpp:935
    /// </summary>
    private void ConfigureResampler() {
        // Reset all resampling flags
        _doLerpUpsample = false;
        _doZohUpsample = false;
        
        // Update Speex resampler rates (will be used only when needed)
        _speexResampler.SetRate((uint)_sampleRateHz, (uint)_mixerSampleRateHz);
        
        switch (_resampleMethod) {
            case ResampleMethod.LerpUpsampleOrResample:
                if (_sampleRateHz < _mixerSampleRateHz) {
                    _doLerpUpsample = true;
                    InitLerpUpsamplerState();
                } else if (_sampleRateHz > _mixerSampleRateHz) {
                    // Use Speex for downsampling - mirrors DOSBox behavior
                    LogSpeexInitialization();
                }
                break;
                
            case ResampleMethod.ZeroOrderHoldAndResample:
                if (_sampleRateHz < _zohTargetRateHz) {
                    _doZohUpsample = true;
                    InitZohUpsamplerState();
                } else if (_sampleRateHz != _mixerSampleRateHz) {
                    // Use Speex for any rate conversion after ZoH - mirrors DOSBox
                    LogSpeexInitialization();
                }
                break;
                
            case ResampleMethod.Resample:
                // Speex-only resampling for all rate conversions
                if (_sampleRateHz != _mixerSampleRateHz) {
                    LogSpeexInitialization();
                }
                break;
        }
    }
    
    /// <summary>
    /// Logs Speex resampler initialization for debugging.
    /// </summary>
    private void LogSpeexInitialization() {
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Debug)) {
            _loggerService.Debug(
                "MIXER: Channel {Name} using Speex resampler (C#) {InRate}Hz -> {OutRate}Hz",
                _name, _sampleRateHz, _mixerSampleRateHz);
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
    /// Enables or disables the channel.
    /// </summary>
    public void Enable(bool shouldEnable) {
        if (IsEnabled == shouldEnable) {
            return;
        }

        lock (_mutex) {
            if (!shouldEnable) {
                // Clear state when disabling
                _framesNeeded = 0;
                AudioFrames.Clear();
                _prevFrame = new AudioFrame(0.0f, 0.0f);
                _nextFrame = new AudioFrame(0.0f, 0.0f);
            }

            IsEnabled = shouldEnable;
        }
    }

    /// <summary>
    /// Requests frames from the channel handler and fills the audio buffer.
    /// Mirrors the Mix() method from DOSBox with resampling support.
    /// </summary>
    public void Mix(int framesRequested) {
        if (!IsEnabled) {
            return;
        }

        _framesNeeded = framesRequested;

        // If resampling is enabled, request proportionally more/fewer frames
        lock (_mutex) {
            if (_doLerpUpsample && _sampleRateHz < _mixerSampleRateHz) {
                // Need to upsample - request fewer input frames
                int inputFramesNeeded = (int)Math.Ceiling(
                    framesRequested * (double)_sampleRateHz / _mixerSampleRateHz);
                
                // Request frames from handler
                if (inputFramesNeeded > 0) {
                    _handler(inputFramesNeeded);
                }
                
                // Perform linear interpolation upsampling
                ApplyLerpUpsampling(framesRequested);
            } else if (_speexResampler.IsInitialized && _sampleRateHz != _mixerSampleRateHz) {
                // Use Speex resampler for high-quality rate conversion
                // Calculate how many input frames we need to produce the requested output
                int inputFramesNeeded = (int)Math.Ceiling(
                    framesRequested * (double)_sampleRateHz / _mixerSampleRateHz);
                
                // Request frames from handler
                if (inputFramesNeeded > 0) {
                    _handler(inputFramesNeeded);
                }
                
                // Apply Speex resampling on the collected buffer
                SpeexResampleBuffer(framesRequested);
            } else {
                // No resampling or downsampling - request frames directly
                while (_framesNeeded > AudioFrames.Count) {
                    float stretchFactor = (float)_sampleRateHz / _mixerSampleRateHz;
                    int framesRemaining = (int)Math.Ceiling(
                        (_framesNeeded - AudioFrames.Count) * stretchFactor);

                    if (framesRemaining <= 0) {
                        break;
                    }

                    _handler(Math.Max(1, framesRemaining));
                }
            }
        }
    }
    
    /// <summary>
    /// Applies linear interpolation upsampling to reach the mixer sample rate.
    /// Mirrors DOSBox lerp_upsample logic.
    /// </summary>
    private void ApplyLerpUpsampling(int targetFrames) {
        // Store input frames and clear buffer for output
        List<AudioFrame> inputFrames = new List<AudioFrame>(AudioFrames);
        AudioFrames.Clear();
        
        // If no input, fill with silence or last known frame
        if (inputFrames.Count == 0) {
            for (int i = 0; i < targetFrames; i++) {
                AudioFrames.Add(_lerpPrevFrame); // Use last frame or silence
            }
            return;
        }
        
        double stepSize = (double)_sampleRateHz / _mixerSampleRateHz;
        int inputIndex = 0;
        
        // Always produce targetFrames output
        for (int i = 0; i < targetFrames; i++) {
            // Ensure we have valid prev and next frames
            if (inputIndex < inputFrames.Count) {
                _lerpNextFrame = inputFrames[inputIndex];
            }
            // else keep using the last _lerpNextFrame
            
            // Linear interpolation
            float t = (float)(_lerpPhase - Math.Floor(_lerpPhase));
            AudioFrame interpolated = new AudioFrame(
                _lerpPrevFrame.Left * (1.0f - t) + _lerpNextFrame.Left * t,
                _lerpPrevFrame.Right * (1.0f - t) + _lerpNextFrame.Right * t
            );
            
            AudioFrames.Add(interpolated);
            
            // Advance phase
            _lerpPhase += stepSize;
            while (_lerpPhase >= 1.0) {
                _lerpPhase -= 1.0;
                _lerpPrevFrame = _lerpNextFrame;
                inputIndex++;
                if (inputIndex < inputFrames.Count) {
                    _lerpNextFrame = inputFrames[inputIndex];
                }
            }
        }
    }
    
    /// <summary>
    /// Applies Speex resampling to the collected AudioFrames buffer.
    /// Processes stereo channels separately through the Speex resampler.
    /// Mirrors DOSBox Speex resampling integration from mixer.cpp
    /// </summary>
    /// <param name="targetFrames">Number of output frames required</param>
    private void SpeexResampleBuffer(int targetFrames) {
        if (!_speexResampler.IsInitialized) {
            return;
        }
        
        // Store input frames and clear buffer for output
        List<AudioFrame> inputFrames = new List<AudioFrame>(AudioFrames);
        AudioFrames.Clear();
        
        // If no input, fill with silence
        if (inputFrames.Count == 0) {
            for (int i = 0; i < targetFrames; i++) {
                AudioFrames.Add(new AudioFrame(0.0f, 0.0f));
            }
            return;
        }
        
        try {
            // Prepare separate channel buffers for Speex processing
            // Speex processes each channel independently
            int inputCount = inputFrames.Count;
            float[] leftInput = new float[inputCount];
            float[] rightInput = new float[inputCount];
            float[] leftOutput = new float[targetFrames];
            float[] rightOutput = new float[targetFrames];
            
            // Extract left and right channels from AudioFrames
            for (int i = 0; i < inputCount; i++) {
                leftInput[i] = inputFrames[i].Left;
                rightInput[i] = inputFrames[i].Right;
            }
            
            // Process left channel (channel index 0)
            _speexResampler.ProcessFloat(
                0, // Left channel
                leftInput.AsSpan(),
                leftOutput.AsSpan(),
                out uint leftConsumed,
                out uint leftGenerated);
            
            // Process right channel (channel index 1)
            _speexResampler.ProcessFloat(
                1, // Right channel
                rightInput.AsSpan(),
                rightOutput.AsSpan(),
                out uint rightConsumed,
                out uint rightGenerated);
            
            // Rebuild AudioFrames with resampled data
            // Use the minimum of left/right generated frames to keep channels synchronized
            int outputCount = (int)Math.Min(leftGenerated, rightGenerated);
            
            for (int i = 0; i < outputCount; i++) {
                AudioFrames.Add(new AudioFrame(leftOutput[i], rightOutput[i]));
            }
            
            // If we didn't produce enough frames, pad with the last frame or silence
            while (AudioFrames.Count < targetFrames) {
                AudioFrame lastFrame;
                if (AudioFrames.Count > 0) {
                    lastFrame = AudioFrames[AudioFrames.Count - 1];
                } else {
                    lastFrame = new AudioFrame(0.0f, 0.0f);
                }
                AudioFrames.Add(lastFrame);
            }
            
            // If we produced too many frames, truncate to target
            if (AudioFrames.Count > targetFrames) {
                AudioFrames.RemoveRange(targetFrames, AudioFrames.Count - targetFrames);
            }
            
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
                _loggerService.Verbose(
                    "MIXER: Channel {Name} Speex resampled {InputFrames} -> {OutputFrames} frames " +
                    "(L consumed: {LeftConsumed}, R consumed: {RightConsumed})",
                    _name, inputCount, AudioFrames.Count, leftConsumed, rightConsumed);
            }
        } catch (Exception ex) {
            // If Speex resampling fails, log error and fall back to pass-through
            _loggerService.Error(
                "MIXER: Channel {Name} Speex resampling failed: {Error}. Falling back to pass-through.",
                _name, ex.Message);
            
            // Restore original frames or pad/truncate as needed
            AudioFrames.Clear();
            for (int i = 0; i < Math.Min(inputFrames.Count, targetFrames); i++) {
                AudioFrames.Add(inputFrames[i]);
            }
            
            // Pad if necessary
            while (AudioFrames.Count < targetFrames) {
                AudioFrame lastFrame;
                if (AudioFrames.Count > 0) {
                    lastFrame = AudioFrames[AudioFrames.Count - 1];
                } else {
                    lastFrame = new AudioFrame(0.0f, 0.0f);
                }
                AudioFrames.Add(lastFrame);
            }
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
                    
                    AudioFrame baseFrame;
                    if (_lastSamplesWereStereo) {
                        baseFrame = _prevFrame;
                    } else {
                        baseFrame = new AudioFrame(_prevFrame.Left);
                    }
                    frameWithGain = baseFrame.Multiply(_combinedVolumeGain);
                    
                    _prevFrame = _nextFrame;
                }

                AudioFrame outFrame = new();
                outFrame[(int)_outputMap.Left] = frameWithGain.Left;
                outFrame[(int)_outputMap.Right] = frameWithGain.Right;
                
                AudioFrames.Add(outFrame);
            }
        }
    }

    /// <summary>
    /// Converts and adds a single frame with optional ZoH upsampling.
    /// Mirrors DOSBox ConvertSamplesAndMaybeZohUpsample() logic from mixer.cpp:1871
    /// </summary>
    private void ConvertSamplesAndMaybeZohUpsample(AudioFrame frame, bool isStereo) {
        _prevFrame = _nextFrame;
        _nextFrame = frame;
        
        AudioFrame frameWithGain;
        if (isStereo) {
            frameWithGain = new AudioFrame(
                _prevFrame[(int)_channelMap.Left],
                _prevFrame[(int)_channelMap.Right]
            );
        } else {
            frameWithGain = new AudioFrame(_prevFrame[(int)_channelMap.Left]);
        }
        
        frameWithGain = frameWithGain.Multiply(_combinedVolumeGain);
        
        AudioFrame outFrame = new();
        outFrame[(int)_outputMap.Left] = frameWithGain.Left;
        outFrame[(int)_outputMap.Right] = frameWithGain.Right;
        
        AudioFrames.Add(outFrame);
    }
    
    /// <summary>
    /// Adds mono 8-bit unsigned samples with optional ZoH upsampling.
    /// </summary>
    public void AddSamples_m8(int numFrames, ReadOnlySpan<byte> data) {
        lock (_mutex) {
            int pos = 0;
            while (pos < numFrames && pos < data.Length) {
                float sample = LookupTables.U8To16[data[pos]];
                AudioFrame frame = new(sample, sample);
                
                ConvertSamplesAndMaybeZohUpsample(frame, false);
                
                if (_doZohUpsample) {
                    _zohPos += _zohStep;
                    while (_zohPos > 1.0f) {
                        _zohPos -= 1.0f;
                        pos++;
                        if (pos >= numFrames || pos >= data.Length) {
                            break;
                        }
                    }
                } else {
                    pos++;
                }
            }
            
            _lastSamplesWereStereo = false;
        }
    }

    /// <summary>
    /// Adds mono 16-bit signed samples with optional ZoH upsampling.
    /// </summary>
    public void AddSamples_m16(int numFrames, ReadOnlySpan<short> data) {
        lock (_mutex) {
            int pos = 0;
            while (pos < numFrames && pos < data.Length) {
                float sample = data[pos];
                AudioFrame frame = new(sample, sample);
                
                ConvertSamplesAndMaybeZohUpsample(frame, false);
                
                if (_doZohUpsample) {
                    _zohPos += _zohStep;
                    while (_zohPos > 1.0f) {
                        _zohPos -= 1.0f;
                        pos++;
                        if (pos >= numFrames || pos >= data.Length) {
                            break;
                        }
                    }
                } else {
                    pos++;
                }
            }
            
            _lastSamplesWereStereo = false;
        }
    }

    /// <summary>
    /// Adds stereo 16-bit signed samples with optional ZoH upsampling.
    /// </summary>
    public void AddSamples_s16(int numFrames, ReadOnlySpan<short> data) {
        lock (_mutex) {
            int pos = 0;
            while (pos < numFrames && (pos * 2 + 1) < data.Length) {
                float left = data[pos * 2];
                float right = data[pos * 2 + 1];
                AudioFrame frame = new(left, right);
                
                ConvertSamplesAndMaybeZohUpsample(frame, true);
                
                if (_doZohUpsample) {
                    _zohPos += _zohStep;
                    while (_zohPos > 1.0f) {
                        _zohPos -= 1.0f;
                        pos++;
                        if (pos >= numFrames || (pos * 2 + 1) >= data.Length) {
                            break;
                        }
                    }
                } else {
                    pos++;
                }
            }
            
            _lastSamplesWereStereo = true;
        }
    }

    /// <summary>
    /// Adds mono 32-bit float samples with optional ZoH upsampling.
    /// Mirrors DOSBox AddSamples_mfloat() from mixer.cpp:2287
    /// </summary>
    public void AddSamples_mfloat(int numFrames, ReadOnlySpan<float> data) {
        lock (_mutex) {
            int pos = 0;
            while (pos < numFrames && pos < data.Length) {
                float sample = data[pos] * 32768.0f; // Convert normalized float to 16-bit range
                AudioFrame frame = new(sample, sample);
                
                ConvertSamplesAndMaybeZohUpsample(frame, false);
                
                if (_doZohUpsample) {
                    _zohPos += _zohStep;
                    while (_zohPos > 1.0f) {
                        _zohPos -= 1.0f;
                        pos++;
                        if (pos >= numFrames || pos >= data.Length) {
                            break;
                        }
                    }
                } else {
                    pos++;
                }
            }
            
            _lastSamplesWereStereo = false;
        }
    }
    
    /// <summary>
    /// Adds stereo 32-bit float samples with optional ZoH upsampling.
    /// Mirrors DOSBox AddSamples_sfloat() from mixer.cpp:2292
    /// </summary>
    public void AddSamples_sfloat(int numFrames, ReadOnlySpan<float> data) {
        lock (_mutex) {
            int pos = 0;
            while (pos < numFrames && (pos * 2 + 1) < data.Length) {
                float left = data[pos * 2] * 32768.0f; // Convert normalized float to 16-bit range
                float right = data[pos * 2 + 1] * 32768.0f;
                AudioFrame frame = new(left, right);
                
                ConvertSamplesAndMaybeZohUpsample(frame, true);
                
                if (_doZohUpsample) {
                    _zohPos += _zohStep;
                    while (_zohPos > 1.0f) {
                        _zohPos -= 1.0f;
                        pos++;
                        if (pos >= numFrames || (pos * 2 + 1) >= data.Length) {
                            break;
                        }
                    }
                } else {
                    pos++;
                }
            }
            
            _lastSamplesWereStereo = true;
        }
    }

    /// <summary>
    /// Adds audio frames directly with optional ZoH upsampling.
    /// </summary>
    public void AddAudioFrames(ReadOnlySpan<AudioFrame> frames) {
        lock (_mutex) {
            int pos = 0;
            while (pos < frames.Length) {
                AudioFrame frame = frames[pos];
                
                ConvertSamplesAndMaybeZohUpsample(frame, true);
                
                if (_doZohUpsample) {
                    _zohPos += _zohStep;
                    while (_zohPos > 1.0f) {
                        _zohPos -= 1.0f;
                        pos++;
                        if (pos >= frames.Length) {
                            break;
                        }
                    }
                } else {
                    pos++;
                }
            }
            
            _lastSamplesWereStereo = true;
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
