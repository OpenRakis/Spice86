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

    // Volume gains - mirrors DOSBox volume system
    private AudioFrame _userVolumeGain = new(1.0f, 1.0f);
    private AudioFrame _appVolumeGain = new(1.0f, 1.0f);
    private float _db0VolumeGain = 1.0f;
    private AudioFrame _combinedVolumeGain = new(1.0f, 1.0f);

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
            // TODO: Configure resampler if needed
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
    /// Mirrors the Mix() method from DOSBox.
    /// </summary>
    public void Mix(int framesRequested) {
        if (!IsEnabled) {
            return;
        }

        _framesNeeded = framesRequested;

        while (_framesNeeded > AudioFrames.Count) {
            float stretchFactor;
            int framesRemaining;
            
            lock (_mutex) {
                stretchFactor = (float)_sampleRateHz / 48000.0f; // TODO: Get mixer rate
                framesRemaining = (int)Math.Ceiling(
                    (_framesNeeded - AudioFrames.Count) * stretchFactor);

                if (framesRemaining <= 0) {
                    break;
                }
            }

            _handler(Math.Max(1, framesRemaining));
        }
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
    /// Adds mono 8-bit unsigned samples.
    /// </summary>
    public void AddSamples_m8(int numFrames, ReadOnlySpan<byte> data) {
        lock (_mutex) {
            for (int i = 0; i < numFrames && i < data.Length; i++) {
                float sample = LookupTables.U8To16[data[i]];
                AudioFrame frame = new(sample * _combinedVolumeGain.Left, 
                                      sample * _combinedVolumeGain.Right);
                
                AudioFrame outFrame = new();
                outFrame[(int)_outputMap.Left] = frame.Left;
                outFrame[(int)_outputMap.Right] = frame.Right;
                
                AudioFrames.Add(outFrame);
            }
            
            _lastSamplesWereStereo = false;
        }
    }

    /// <summary>
    /// Adds mono 16-bit signed samples.
    /// </summary>
    public void AddSamples_m16(int numFrames, ReadOnlySpan<short> data) {
        lock (_mutex) {
            for (int i = 0; i < numFrames && i < data.Length; i++) {
                float sample = data[i];
                AudioFrame frame = new(sample * _combinedVolumeGain.Left,
                                      sample * _combinedVolumeGain.Right);
                
                AudioFrame outFrame = new();
                outFrame[(int)_outputMap.Left] = frame.Left;
                outFrame[(int)_outputMap.Right] = frame.Right;
                
                AudioFrames.Add(outFrame);
            }
            
            _lastSamplesWereStereo = false;
        }
    }

    /// <summary>
    /// Adds stereo 16-bit signed samples.
    /// </summary>
    public void AddSamples_s16(int numFrames, ReadOnlySpan<short> data) {
        lock (_mutex) {
            for (int i = 0; i < numFrames && (i * 2 + 1) < data.Length; i++) {
                float left = data[i * 2];
                float right = data[i * 2 + 1];
                
                AudioFrame frame = new(left * _combinedVolumeGain.Left,
                                      right * _combinedVolumeGain.Right);
                
                AudioFrame outFrame = new();
                outFrame[(int)_outputMap.Left] = frame[(int)_channelMap.Left];
                outFrame[(int)_outputMap.Right] = frame[(int)_channelMap.Right];
                
                AudioFrames.Add(outFrame);
            }
            
            _lastSamplesWereStereo = true;
        }
    }

    /// <summary>
    /// Adds audio frames directly.
    /// </summary>
    public void AddAudioFrames(ReadOnlySpan<AudioFrame> frames) {
        lock (_mutex) {
            foreach (AudioFrame frame in frames) {
                AudioFrame scaledFrame = new(
                    frame.Left * _combinedVolumeGain.Left,
                    frame.Right * _combinedVolumeGain.Right
                );
                
                AudioFrame outFrame = new();
                outFrame[(int)_outputMap.Left] = scaledFrame[(int)_channelMap.Left];
                outFrame[(int)_outputMap.Right] = scaledFrame[(int)_channelMap.Right];
                
                AudioFrames.Add(outFrame);
            }
            
            _lastSamplesWereStereo = true;
        }
    }
}
