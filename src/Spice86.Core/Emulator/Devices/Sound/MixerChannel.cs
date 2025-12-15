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
public sealed class MixerChannel {
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

    public MixerChannel(
        Action<int> handler,
        string name,
        HashSet<ChannelFeature> features,
        ILoggerService loggerService) {
        
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _name = name ?? throw new ArgumentNullException(nameof(name));
        _features = features ?? throw new ArgumentNullException(nameof(features));
        _loggerService = loggerService ?? throw new ArgumentNullException(nameof(loggerService));
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
        
        switch (_resampleMethod) {
            case ResampleMethod.LerpUpsampleOrResample:
                if (_sampleRateHz < _mixerSampleRateHz) {
                    _doLerpUpsample = true;
                    InitLerpUpsamplerState();
                }
                // Note: Speex downsampling not implemented yet
                break;
                
            case ResampleMethod.ZeroOrderHoldAndResample:
                if (_sampleRateHz < _zohTargetRateHz) {
                    _doZohUpsample = true;
                    InitZohUpsamplerState();
                }
                // Note: Speex resampling not implemented yet
                break;
                
            case ResampleMethod.Resample:
                // Note: Speex-only resampling not implemented yet
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
                    float nextLeft = _prevFrame.Left > fadeAmount ? _prevFrame.Left - fadeAmount :
                                     _prevFrame.Left < -fadeAmount ? _prevFrame.Left + fadeAmount : 0.0f;
                    float nextRight = _prevFrame.Right > fadeAmount ? _prevFrame.Right - fadeAmount :
                                      _prevFrame.Right < -fadeAmount ? _prevFrame.Right + fadeAmount : 0.0f;
                    
                    _nextFrame = new AudioFrame(nextLeft, nextRight);
                    
                    frameWithGain = (_lastSamplesWereStereo ? _prevFrame : new AudioFrame(_prevFrame.Left))
                        .Multiply(_combinedVolumeGain);
                    
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
    private void ConvertAndAddFrame(AudioFrame frame, bool isStereo) {
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
                
                ConvertAndAddFrame(frame, false);
                
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
                
                ConvertAndAddFrame(frame, false);
                
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
                
                ConvertAndAddFrame(frame, true);
                
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
                
                ConvertAndAddFrame(frame, false);
                
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
                
                ConvertAndAddFrame(frame, true);
                
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
                
                ConvertAndAddFrame(frame, true);
                
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
}
